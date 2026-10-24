using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Elynt.App.Theme;
using Elynt.App.ViewModels;

namespace Elynt.App.Screenshots;

/// <summary>Walks every page of the window at a few sizes, saving what each one looks like.</summary>
internal static partial class Tour
{
    private static readonly (string Name, double Width, double Height)[] Sizes =
    [
        ("default", 1600, 1000), // the size the window opens at, where the screen has room
        ("smallest", 900, 520), // its minimum size
        ("wide", 2560, 1400), // a 1440p screen, maximised
    ];

    public static async Task RunAsync(Application app, string output)
    {
        await WaitUntilAsync(() => app.MainWindow is MainWindow { IsLoaded: true }, TimeSpan.FromSeconds(30), () => "the window to open");
        var window = (MainWindow)app.MainWindow;
        (window.Left, window.Top) = (0, 0);
        var model = (MainViewModel)window.DataContext;
        await WaitUntilAsync(() => model.IsConnected, TimeSpan.FromSeconds(30), () => $"the window to connect (status: {model.Status})");

        Console.WriteLine($"Opened at {window.Width}x{window.Height} on a {SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height} work area, theme {Application.Current.ThemeMode.Value}");

        // There is one theme, so one pass per size.
        foreach (var size in Sizes)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            var folder = Path.Combine(output, size.Name);
            Directory.CreateDirectory(folder);
            // Real screen pixels too (title bar, backdrop, leftover drawing), once per page.
            await ShootPagesAsync(window, model, folder, screen: size.Name == "default");
        }

        await ShootConfirmationAsync(window, model, output);
        await CheckWaveHoldsStillAsync(window, model);
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
    /// A confirmation dialog: the one a destructive tweak asks before it is applied, over the window at its default
    /// size. Shown without waiting for an answer, then closed.
    /// </summary>
    private static async Task ShootConfirmationAsync(MainWindow window, MainViewModel model, string output)
    {
        var size = Sizes[0];
        (window.Width, window.Height) = (size.Width, size.Height);
        TweakItemViewModel? destructive = null;
        foreach (var page in model.Pages)
        {
            model.SelectedPage = page;
            destructive = model.Tweaks.FirstOrDefault(t => t.View.DestructiveWarning is not null);
            if (destructive is not null)
                break;
        }
        if (destructive is null)
            throw new InvalidOperationException("The sample PC has no destructive tweak to confirm.");
        await SettleAsync();

        var dialog = window.DestructiveTweakDialog(destructive.View);
        dialog.Show();
        await SettleAsync();
        Shoot(dialog, Path.Combine(output, size.Name), "confirmation-destructive-tweak", screen: true);
        dialog.Close();
        await SettleAsync();
    }

    /// <summary>
    /// The accent wave must stop while the window is minimised, and when the Settings page's Animated accents switch is
    /// turned off. The switch saves its value, which goes to the tour's own settings file (see Program).
    /// </summary>
    private static async Task CheckWaveHoldsStillAsync(Window window, MainViewModel model)
    {
        // CI machines often have Windows' animation effects off, which holds the wave still too.
        Console.WriteLine($"Accent wave moving while the window is open: {AccentWave.IsMoving} (Windows animations on: {SystemParameters.ClientAreaAnimation}, Animated accents: {model.AnimatedAccents})");
        window.WindowState = WindowState.Minimized;
        await SettleAsync();
        if (AccentWave.IsMoving)
            throw new InvalidOperationException("The accent wave kept moving while the window was minimised.");
        window.WindowState = WindowState.Normal;
        window.Activate();
        model.SelectedPage = MainViewModel.Settings;
        // On first, so turning it off is a real change even where it starts off (Windows' animation effects off).
        var animatedAccents = model.AnimatedAccents;
        model.AnimatedAccents = true;
        await SettleAsync();
        Console.WriteLine($"Accent wave moving with Animated accents on and the window in front: {AccentWave.IsMoving}");
        model.AnimatedAccents = false;
        await SettleAsync();
        if (AccentWave.IsEnabled || AccentWave.IsMoving)
            throw new InvalidOperationException("The accent wave kept moving after Animated accents was turned off.");
        model.AnimatedAccents = animatedAccents;
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
