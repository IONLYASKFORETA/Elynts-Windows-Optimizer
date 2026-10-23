using System.Diagnostics;
using System.Text;

namespace Elynt.App.Screenshots;

/// <summary>
/// Collects WPF's data binding errors. A binding that resolves against the wrong object fails silently in the app
/// (a detail panel that never hides, say), so the tour reports every one it hits, apart from WPF's own known noise.
/// </summary>
internal sealed class BindingErrorLog : TraceListener
{
    private readonly StringBuilder _line = new();
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors.Distinct().ToList();

    /// <summary>Turns WPF's binding tracing on (it is off without a debugger) and listens for errors.</summary>
    public static BindingErrorLog Listen()
    {
        var log = new BindingErrorLog();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(log);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        return log;
    }

    public override void Write(string? message) => _line.Append(message);

    public override void WriteLine(string? message)
    {
        _line.Append(message);
        var error = _line.ToString();
        _line.Clear();
        if (!IsFrameworkNoise(error))
            _errors.Add(error);
    }

    /// <summary>
    /// The built-in item styles bind an item's content alignment to its list, and log error 4 when a list item is
    /// removed before that binding runs (every time a page swaps its list). Harmless, and not from this app's XAML.
    /// </summary>
    private static bool IsFrameworkNoise(string error) =>
        error.Contains("AncestorType='System.Windows.Controls.ItemsControl'", StringComparison.Ordinal) &&
        (error.Contains("Path=HorizontalContentAlignment;", StringComparison.Ordinal) ||
         error.Contains("Path=VerticalContentAlignment;", StringComparison.Ordinal));
}
