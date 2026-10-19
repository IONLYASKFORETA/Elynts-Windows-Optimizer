namespace Elynt.Core.Apps;

/// <summary>
/// Windows optional features ("Turn Windows features on or off"). The real adapter wraps DISM; the fake holds
/// in-memory state. Features are turned off without removing their files, so turning one back on needs no download.
/// </summary>
public interface IOptionalFeatures
{
    /// <summary>Whether the feature is turned on, or null when this PC doesn't have it at all.</summary>
    bool? IsEnabled(string name);

    void Enable(string name);

    void Disable(string name);
}

/// <summary>Stands in when no feature adapter is wired up: this PC has no optional features.</summary>
public sealed class NoOptionalFeatures : IOptionalFeatures
{
    public bool? IsEnabled(string name) => null;

    public void Enable(string name) => throw new NotSupportedException("Windows features aren't available here.");

    public void Disable(string name) => throw new NotSupportedException("Windows features aren't available here.");
}
