using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Elynt.App.Theme;

/// <summary>
/// The accent wave: one red-orange-red gradient, shared by every accent, that repeats every 480 pixels and slides
/// along once every 4 seconds. Wide accents (the top strip) show it travelling; small ones (a switch) drift between
/// red and orange. Button faces get a twin that turns a darker orange, so their white labels stay readable, and moves
/// in step with it. It moves only while the window is in front and not minimised, Windows' animation effects are on
/// and the wave hasn't been turned off; otherwise it holds still where it is, so the app costs a game nothing.
/// </summary>
public static class AccentWave
{
    /// <summary>The resource key accents use, always with DynamicResource.</summary>
    public const string Key = "Theme.AccentWave";

    /// <summary>The resource key primary buttons' faces use, always with DynamicResource.</summary>
    public const string ButtonKey = "Theme.ButtonWave";

    private const double Period = 480;
    private static AnimationClock? _clock;
    private static Window? _window;
    private static bool _isEnabled = true;

    /// <summary>Whether the wave may move at all (the "Animated accents" setting).</summary>
    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            Update();
        }
    }

    /// <summary>Whether the wave is moving right now.</summary>
    public static bool IsMoving => _clock is { IsPaused: false };

    /// <summary>Puts the moving waves in the app's resources, where they win over Theme.xaml's still ones.</summary>
    public static void Install(Application app)
    {
        // One slide for both waves, so a button and the strip above it stay in step.
        var slide = new TranslateTransform();
        var red = Color.FromRgb(0xE5, 0x16, 0x2D);
        var wave = Wave(slide, red, Color.FromRgb(0xFF, 0x7A, 0x1A));
        // 4.6:1 with white at its orange middle, against 2.6:1 for the accent wave's bright orange.
        var buttonWave = Wave(slide, red, Color.FromRgb(0xCC, 0x4A, 0x0A));
        _clock = new DoubleAnimation(0, Period, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever }.CreateClock();
        slide.ApplyAnimationClock(TranslateTransform.XProperty, _clock);
        _clock.Controller.Pause();
        app.Resources[Key] = wave;
        app.Resources[ButtonKey] = buttonWave;
        // Progress bars use Fluent's own style, which fills them with this key.
        app.Resources["ProgressBarForeground"] = wave;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    /// <summary>A gradient from <paramref name="ends"/> to <paramref name="middle"/> and back, repeating every period.</summary>
    private static LinearGradientBrush Wave(Transform slide, Color ends, Color middle) => new()
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Point(0, 0),
        EndPoint = new Point(Period, 0),
        SpreadMethod = GradientSpreadMethod.Repeat,
        Transform = slide,
        GradientStops =
        {
            new GradientStop(ends, 0),
            new GradientStop(middle, 0.5),
            new GradientStop(ends, 1),
        },
    };

    /// <summary>Moves the wave only while this window is in front and not minimised.</summary>
    public static void Follow(Window window)
    {
        _window = window;
        window.Activated += (_, _) => Update();
        window.Deactivated += (_, _) => Update();
        window.StateChanged += (_, _) => Update();
        window.Closed += (_, _) => Update();
        Update();
    }

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Update();
    }

    private static void Update()
    {
        if (_clock is null)
            return;
        var move = _isEnabled && SystemParameters.ClientAreaAnimation &&
                   _window is { IsActive: true, WindowState: not WindowState.Minimized, IsVisible: true };
        if (move)
            _clock.Controller.Resume();
        else
            _clock.Controller.Pause();
    }
}
