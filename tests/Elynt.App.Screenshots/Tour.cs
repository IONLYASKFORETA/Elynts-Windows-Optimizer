using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Elynt.App.Prototype;
using Elynt.App.ViewModels;
using Elynt.Core.Tweaks;

namespace Elynt.App.Screenshots;

/// <summary>Walks every page of the window at a few sizes and in both themes, saving what each one looks like.</summary>
internal static partial class Tour
{
    private static readonly (string Name, double Width, double Height)[] Sizes =
    [
        ("default", 1180, 720), // the size the window opens at
        ("smallest", 900, 520), // its minimum size
        ("wide", 1600, 900),
    ];

    public static async Task RunAsync(Application app, string output, bool prototype)
    {
        await WaitUntilAsync(() => app.MainWindow is MainWindow { IsLoaded: true }, TimeSpan.FromSeconds(30), () => "the window to open");
        var window = (MainWindow)app.MainWindow;
        (window.Left, window.Top) = (0, 0);
        var model = (MainViewModel)window.DataContext;
        await WaitUntilAsync(() => model.IsConnected, TimeSpan.FromSeconds(30), () => $"the window to connect (status: {model.Status})");

        if (prototype)
        {
            await ShootPrototypeAsync(window, model, output);
            window.Close();
            return;
        }

        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            Application.Current.ThemeMode = theme;
            foreach (var size in theme == ThemeMode.Light ? Sizes : Sizes[..1])
            {
                window.Width = size.Width;
                window.Height = size.Height;
                var folder = Path.Combine(output, $"{theme.Value.ToLowerInvariant()}-{size.Name}");
                Directory.CreateDirectory(folder);
                // Real screen pixels too (title bar, backdrop, leftover drawing), once per page.
                var screen = theme == ThemeMode.Light && size.Name == "default";
                await ShootPagesAsync(window, model, folder, screen);
            }
        }
        window.Close();
    }

    private static async Task ShootPagesAsync(MainWindow window, MainViewModel model, string folder, bool screen)
    {
        for (var i = 0; i < model.Pages.Count; i++)
        {
            var page = model.Pages[i];
            model.SelectedPage = page;
            await SettleAsync();
            var name = $"{i + 1:00}-{Slug(page)}";
            Shoot(window, folder, name, screen);

            // The detail pane at its fullest: the tweak, then the repair tool, with the most to say.
            if (model.HasSelection && model.Tweaks.Count > 1)
            {
                model.SelectedTweak = model.Tweaks.MaxBy(DetailLength);
                await SettleAsync();
                Shoot(window, folder, $"{name}-fullest-tweak", screen);
            }
            if (model.HasRepairSelection && model.RepairTools.Count > 1)
            {
                model.SelectedRepairTool = model.RepairTools.MaxBy(t => t.View.WhatItDoes.Length + t.View.ExactCommands.Sum(c => c.Length));
                await SettleAsync();
                Shoot(window, folder, $"{name}-fullest-repair-tool", screen);
            }
        }
    }

    /// <summary>
    /// PROTOTYPE, throwaway: each black-and-red layout at two sizes, on Home, a tweak section with its fullest tweak
    /// open and a second tweak section. The switcher is hidden and the mouse moved off the window, so neither the
    /// switcher nor a hover highlight shows in the pictures.
    /// </summary>
    private static async Task ShootPrototypeAsync(MainWindow window, MainViewModel model, string output)
    {
        var shell = PrototypeShell.Current ?? throw new InvalidOperationException("The window didn't start in prototype mode.");
        shell.ShowSwitcher = false;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(screen.Right - 1, screen.Bottom - 1);
        string[] pages = [PrototypeViewModel.Home, Sections.Gaming, Sections.DebloatAndPrivacy];
        foreach (var size in new[] { Sizes[0], Sizes[2] })
        {
            window.Width = size.Width;
            window.Height = size.Height;
            foreach (var (variant, _) in PrototypeShell.Variants)
            {
                shell.Show(variant);
                var folder = Path.Combine(output, $"prototype-{variant.ToLowerInvariant()}-{size.Name}");
                Directory.CreateDirectory(folder);
                for (var i = 0; i < pages.Length; i++)
                {
                    shell.Model.Open(pages[i]);
                    if (pages[i] == Sections.Gaming && model.Tweaks.Count > 1)
                        model.SelectedTweak = model.Tweaks.MaxBy(DetailLength);
                    await SettleAsync();
                    ScrollSelectionIntoView(window);
                    await SettleAsync();
                    Shoot(window, folder, $"{i + 1:00}-{Slug(pages[i])}", screen: false);
                }
            }
        }
    }

    /// <summary>Brings each list's selected item into view, as a click on it would have.</summary>
    private static void ScrollSelectionIntoView(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBox { IsVisible: true, SelectedItem: { } selected } list)
                list.ScrollIntoView(selected);
            ScrollSelectionIntoView(child);
        }
    }

    private static int DetailLength(TweakItemViewModel tweak) =>
        tweak.View.WhatItDoes.Length + tweak.View.Evidence.Length + tweak.View.Undo.Length +
        tweak.View.ExactChange.Sum(c => c.Length) + tweak.Warnings.Sum(w => w.Length) + tweak.AntiCheatWarnings.Sum(w => w.Length) +
        (tweak.View.ApplyWarning?.Length ?? 0) + (tweak.View.DestructiveWarning?.Length ?? 0);

    private static void Shoot(Window window, string folder, string name, bool screen)
    {
        Render(window, Path.Combine(folder, $"{name}.png"));
        if (screen)
            CaptureScreen(window, Path.Combine(folder, $"{name}.screen.png"));
        Console.WriteLine($"{Path.GetFileName(folder)}/{name}");
    }

    /// <summary>Draws the window's content as WPF lays it out, at the window's DPI.</summary>
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(window);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Background(window), null, new Rect(size));
            context.DrawRectangle(
                new VisualBrush(root)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(size),
                },
                null,
                new Rect(size));
        }
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// The window's backdrop. The Fluent theme leaves the window transparent for Mica, which a render doesn't
    /// include, so this falls back to the theme's solid background.
    /// </summary>
    private static Brush Background(Window window)
    {
        if (window.Background is SolidColorBrush { Color.A: 255 } solid)
            return solid;
        foreach (var key in new[] { "ApplicationBackgroundBrush", "SolidBackgroundFillColorBaseBrush" })
        {
            if (window.TryFindResource(key) is SolidColorBrush { Color.A: 255 } themed)
                return themed;
        }
        return Application.Current.ThemeMode == ThemeMode.Dark ? Brushes.Black : Brushes.White;
    }

    /// <summary>Copies the window's rectangle off the screen. Skipped where there is no desktop to copy from.</summary>
    private static void CaptureScreen(Window window, string path)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var left = (int)Math.Round(window.Left * dpi.DpiScaleX);
            var top = (int)Math.Round(window.Top * dpi.DpiScaleY);
            var width = (int)Math.Round(window.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Round(window.ActualHeight * dpi.DpiScaleY);
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No screen capture for {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    /// <summary>Lets bindings, layout and the page's own loading (on the fakes, quick) finish.</summary>
    private static async Task SettleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(800);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, Func<string> what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Gave up waiting for {what()}.");
            await Task.Delay(200);
        }
    }

    private static string Slug(string page) => NotLetterOrDigit().Replace(page.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotLetterOrDigit();
}
