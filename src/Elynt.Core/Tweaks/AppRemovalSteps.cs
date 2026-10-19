using Elynt.Core.Apps;
using Elynt.Core.Journal;

namespace Elynt.Core.Tweaks;

/// <summary>Uninstalls a preinstalled app for the user the engine acts for. Undo installs it again.</summary>
public sealed record AppRemovalStep(RemovableApp App) : TweakStep
{
    public override ChangeTarget Target => new PackageTarget(App.PackageName);

    public override string DescribeChange() => App.Kind switch
    {
        AppKind.OneDrive => $"Uninstall {App.Name} for this account (OneDriveSetup.exe /uninstall)",
        _ => $"Uninstall {App.Name} ({App.PackageName}) for this account (Remove-AppxPackage)",
    };

    public override bool IsApplied(SystemAdapters system) => !system.Apps.IsInstalled(App);

    public override StepRecord Capture(SystemAdapters system) => new AppRemovalRecord(App, system.Apps.IsInstalled(App));

    public override void Apply(SystemAdapters system)
    {
        if (system.Apps.IsInstalled(App))
            system.Apps.Remove(App);
    }
}

/// <summary>Whether the app was installed before the app removed it.</summary>
public sealed record AppRemovalRecord(RemovableApp App, bool WasInstalled) : StepRecord
{
    // Reinstalling an app the user put back themselves would be pointless, never harmful, so nothing conflicts.
    public override string? FindConflict(SystemAdapters system) => null;

    public override void Revert(SystemAdapters system)
    {
        if (!WasInstalled || system.Apps.IsInstalled(App))
            return;

        if (App.ReinstallFrom != ReinstallFrom.Winget)
        {
            system.Apps.Reinstall(App);
            return;
        }

        var result = system.Packages.Install(App.ReinstallId!, AppInstaller.CommunitySource, new Progress<double>());
        if (result.Status == PackageInstallStatus.Failed)
            throw new InvalidOperationException(
                $"Couldn't reinstall {App.Name} with winget: {result.Error ?? "it didn't say why"}. " +
                $"Install it from the Apps section or from Microsoft's website.");
    }
}

/// <summary>Stops Windows installing an app for accounts made later. Undo cannot put this back.</summary>
public sealed record AppDeprovisionStep(RemovableApp App) : TweakStep
{
    public override ChangeTarget Target => new PackageTarget(App.PackageName);

    public override string DescribeChange() =>
        $"Stop Windows installing {App.Name} ({App.PackageName}) for new accounts (Remove-AppxProvisionedPackage -Online)";

    public override bool IsApplied(SystemAdapters system) => !system.Apps.IsProvisioned(App);

    public override StepRecord Capture(SystemAdapters system) => new AppDeprovisionRecord(App, system.Apps.IsProvisioned(App));

    public override void Apply(SystemAdapters system)
    {
        if (system.Apps.IsProvisioned(App))
            system.Apps.Deprovision(App);
    }
}

/// <summary>Whether Windows provisioned the app for new accounts before the app stopped it.</summary>
public sealed record AppDeprovisionRecord(RemovableApp App, bool WasProvisioned) : StepRecord
{
    public override string? FindConflict(SystemAdapters system) => null;

    public override void Revert(SystemAdapters system)
    {
        if (!WasProvisioned || system.Apps.IsProvisioned(App))
            return;
        throw new InvalidOperationException(
            $"Windows keeps no copy of {App.Name} to give new accounts, so this can't be put back. Each account can " +
            "still install it from the Microsoft Store; a Windows repair install or a feature update brings it back " +
            "for new accounts.");
    }
}
