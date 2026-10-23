using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Elynt.App.ViewModels;

namespace Elynt.App.Prototype;

/// <summary>
/// PROTOTYPE, throwaway. Swaps the main window's content for one of three black-and-red layouts, with a floating
/// switcher to flip between them. Only happens when the app starts with <c>--prototype</c> (or ELYNT_PROTOTYPE=1),
/// and nothing in the layouts changes the PC: their switches are display only.
/// </summary>
public sealed class PrototypeShell
{
    public static readonly IReadOnlyList<(string Key, string Name)> Variants =
    [
        ("A", "Sidebar, list and detail pane"),
        ("B", "Top tabs and expanding cards"),
        ("C", "Icon rail, tiles and bottom drawer"),
    ];

    private readonly ContentControl _stage = new();
    private readonly TextBlock _label = new() { Foreground = Brushes.White, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
    private readonly Brush _wave;
    private readonly AnimationClock _waveClock;
    private bool _waveOn = true;
    private int _index;

    private PrototypeShell(Window window, MainViewModel model)
    {
        Model = new PrototypeViewModel(model);
        Application.Current.ThemeMode = ThemeMode.Dark;
        window.Background = new SolidColorBrush(Color.FromRgb(0x09, 0x09, 0x0B));
        (_wave, _waveClock) = NewWave();

        var host = new Grid();
        host.Children.Add(_stage);
        host.Children.Add(Switcher());
        window.Content = host;

        // The wave only moves while the window is in front.
        window.Activated += (_, _) => { if (_waveOn) _waveClock.Controller.Resume(); };
        window.Deactivated += (_, _) => _waveClock.Controller.Pause();
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.Left or Key.Right))
                return;
            Show(_index + (e.Key == Key.Left ? -1 : 1));
            e.Handled = true;
        };
        Show(0);
    }

    /// <summary>The shell in this window, when the app started in prototype mode.</summary>
    public static PrototypeShell? Current { get; private set; }

    public PrototypeViewModel Model { get; }

    public string Variant => Variants[_index].Key;

    public static void AttachIfAsked(Window window, MainViewModel model)
    {
        if (Environment.GetCommandLineArgs().Contains("--prototype") || Environment.GetEnvironmentVariable("ELYNT_PROTOTYPE") == "1")
            Current = new PrototypeShell(window, model);
    }

    public void Show(string variant) => Show(Variants.ToList().FindIndex(v => v.Key == variant));

    private void Show(int index)
    {
        _index = (index + Variants.Count) % Variants.Count;
        FrameworkElement view = Variant switch
        {
            "A" => new VariantA(),
            "B" => new VariantB(),
            _ => new VariantC(),
        };
        // Overrides the still stand-in from BlackRed.xaml; entries of the view's own dictionary win over merged ones.
        view.Resources["Proto.AccentWave"] = _wave;
        view.DataContext = Model;
        _stage.Content = view;
        _label.Text = $"Layout {Variant} · {Variants[_index].Name}";
    }

    /// <summary>
    /// The keyboard-style wave: a red-orange-red gradient that repeats every 480 pixels and slides along. Wide
    /// accents show it travelling; small ones (a switch) see their colour drift between red and orange.
    /// </summary>
    private static (Brush, AnimationClock) NewWave()
    {
        const double period = 480;
        var slide = new TranslateTransform();
        var wave = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(period, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            Transform = slide,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xE5, 0x16, 0x2D), 0),
                new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x1A), 0.5),
                new GradientStop(Color.FromRgb(0xE5, 0x16, 0x2D), 1),
            },
        };
        var clock = new DoubleAnimation(0, period, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever }.CreateClock();
        slide.ApplyAnimationClock(TranslateTransform.XProperty, clock);
        return (wave, clock);
    }

    /// <summary>The floating bar at the bottom: which layout this is, arrows to flip, and the wave's on/off.</summary>
    private Border Switcher()
    {
        var previous = new Button { Content = "◀", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Previous layout (Ctrl+Left)" };
        previous.Click += (_, _) => Show(_index - 1);
        var next = new Button { Content = "▶", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Next layout (Ctrl+Right)" };
        next.Click += (_, _) => Show(_index + 1);
        var wave = new CheckBox { Content = "Wave", IsChecked = true, Margin = new Thickness(14, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        wave.Click += (_, _) =>
        {
            _waveOn = wave.IsChecked == true;
            if (_waveOn)
                _waveClock.Controller.Resume();
            else
                _waveClock.Controller.Pause();
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(new TextBlock
        {
            Text = "PROTOTYPE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0),
        });
        bar.Children.Add(previous);
        bar.Children.Add(_label);
        bar.Children.Add(next);
        bar.Children.Add(wave);
        return new Border
        {
            Child = bar,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x16, 0x16, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xE5, 0x16, 0x2D)),
            BorderThickness = new Thickness(1),
        };
    }
}
