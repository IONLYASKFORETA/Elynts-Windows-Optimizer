namespace Elynt.Core.Apps;

/// <summary>How a removed app is installed again.</summary>
public enum AppKind
{
    /// <summary>A Store (Appx/MSIX) package, removed with Remove-AppxPackage for one user.</summary>
    Appx,
    /// <summary>OneDrive's desktop app, removed with its own setup's /uninstall.</summary>
    OneDrive,
}

public enum ReinstallFrom
{
    /// <summary>Re-registered from the copy Windows still keeps; failing that, from the Microsoft Store page.</summary>
    MicrosoftStore,
    /// <summary><c>wsreset -i</c>, Microsoft's own way to put the Microsoft Store back.</summary>
    StoreReset,
    /// <summary>winget's community source, by <see cref="RemovableApp.ReinstallId"/>.</summary>
    Winget,
}

/// <summary>A preinstalled app the Debloat &amp; Privacy section can remove.</summary>
/// <param name="Name">The name people know it by, e.g. "Copilot".</param>
/// <param name="PackageName">The package's name, e.g. Microsoft.Copilot (Appx) or Microsoft.OneDrive.</param>
/// <param name="ReinstallId">The Store product id or winget id that undo installs it from.</param>
public sealed record RemovableApp(
    string Name,
    string PackageName,
    AppKind Kind,
    ReinstallFrom ReinstallFrom,
    string? ReinstallId = null);

/// <summary>
/// Preinstalled apps, for the user the engine acts for and for accounts made later (provisioned packages). The real
/// adapter wraps the Appx cmdlets and OneDrive's setup; the fake holds in-memory state.
/// </summary>
public interface IRemovableApps
{
    /// <summary>Whether the app is installed for this user.</summary>
    bool IsInstalled(RemovableApp app);

    /// <summary>Uninstalls the app for this user. Its data for this user is lost.</summary>
    void Remove(RemovableApp app);

    /// <summary>
    /// Installs the app again for this user (not used for <see cref="ReinstallFrom.Winget"/>). Throws, with what the
    /// user can do instead, when it can't.
    /// </summary>
    void Reinstall(RemovableApp app);

    /// <summary>Whether Windows installs the app for every new account (a provisioned package).</summary>
    bool IsProvisioned(RemovableApp app);

    /// <summary>Stops Windows installing the app for new accounts. Windows keeps no copy to undo this with.</summary>
    void Deprovision(RemovableApp app);
}

/// <summary>Stands in when no app adapter is wired up: nothing is installed or provisioned, and nothing can be changed.</summary>
public sealed class NoRemovableApps : IRemovableApps
{
    public bool IsInstalled(RemovableApp app) => false;

    public void Remove(RemovableApp app) => throw NotHere();

    public void Reinstall(RemovableApp app) => throw NotHere();

    public bool IsProvisioned(RemovableApp app) => false;

    public void Deprovision(RemovableApp app) => throw NotHere();

    private static NotSupportedException NotHere() => new("App removal isn't available here.");
}
