using System.Diagnostics;
using System.Text;

namespace Elynt.App.Screenshots;

/// <summary>
/// Collects WPF's data binding errors. A binding that resolves against the wrong object fails silently in the app
/// (a detail panel that never hides, say), so the tour reports every one it hits.
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
        _errors.Add(_line.ToString());
        _line.Clear();
    }
}
