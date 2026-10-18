using Elynt.Core.Apps;

namespace Elynt.Core.Tweaks;

/// <summary>
/// Debloat &amp; Privacy app removal (spec user stories 46, 47, 49, 51, 53, 54). Removing an app for this account
/// is labelled "needs a download": undo installs it again. Removing it for accounts made later is destructive.
/// Edge, WebView2, the Xbox apps and Game Bar are never offered (the hard rules refuse Edge and WebView2 anyway).
/// </summary>
public static class AppRemovalTweaks
{
    private static readonly Source RemoveAppxDocs = new(
        "Microsoft: Remove-AppxPackage",
        "https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage");

    public static RemovableApp Copilot { get; } =
        new("Copilot", "Microsoft.Copilot", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9NHT9RB2F4HD");

    public static RemovableApp OneDrive { get; } =
        new("OneDrive", "Microsoft.OneDrive", AppKind.OneDrive, ReinstallFrom.Winget, "Microsoft.OneDrive");

    public static RemovableApp MicrosoftStore { get; } =
        new("Microsoft Store", "Microsoft.WindowsStore", AppKind.Appx, ReinstallFrom.StoreReset);

    private const string StoreUndo =
        "Installs the app again for your account: from the copy Windows keeps if it still has one, otherwise from " +
        "the Microsoft Store, which needs an internet connection. Your data in the app is not brought back.";

    public static TweakDefinition RemoveCopilot { get; } = new(
        Id: "debloat.remove-copilot",
        Section: Sections.DebloatAndPrivacy,
        Name: "Remove the Copilot app",
        WhatItDoes:
            "Uninstalls the Copilot app for your account. The Copilot key and taskbar button stop opening it. Other " +
            "accounts on this PC keep it.",
        Evidence:
            "Copilot is a Store app that Windows installs for every account; Remove-AppxPackage uninstalls it for one " +
            "account. A decluttering choice, not a performance one.",
        Sources: [RemoveAppxDocs],
        Tier: EvidenceTier.Proven,
        Undo: StoreUndo,
        Steps: [new AppRemovalStep(Copilot)])
    {
        Reversibility = Reversibility.NeedsDownload,
    };

    public static TweakDefinition RemoveOneDrive { get; } = new(
        Id: "debloat.remove-onedrive",
        Section: Sections.DebloatAndPrivacy,
        Name: "Uninstall OneDrive",
        WhatItDoes:
            "Uninstalls the OneDrive app for your account, so it stops syncing and starting at sign-in. Files already " +
            "downloaded to your OneDrive folder stay where they are; your files in the cloud stay in your Microsoft " +
            "account and are still on onedrive.com.",
        Evidence:
            "OneDrive's own setup program uninstalls it (OneDriveSetup.exe /uninstall); Microsoft documents " +
            "uninstalling OneDrive as a supported choice. A decluttering choice, not a performance one.",
        Sources:
        [
            new("Microsoft: Turn off, disable, or uninstall OneDrive",
                "https://support.microsoft.com/en-us/office/turn-off-disable-or-uninstall-onedrive-f32a17ce-3336-40fe-9c38-6efb09f51924"),
        ],
        Tier: EvidenceTier.Proven,
        Undo:
            "Installs OneDrive again from winget's community source (Microsoft.OneDrive), which needs an internet " +
            "connection. Sign in again to start syncing.",
        Steps: [new AppRemovalStep(OneDrive)])
    {
        Reversibility = Reversibility.NeedsDownload,
        ApplyWarning =
            "Files that are only in the cloud (\"online-only\", with a cloud icon in File Explorer) are not on this PC. " +
            "Once OneDrive is uninstalled you can't open them from File Explorer; they stay safe in the cloud on " +
            "onedrive.com. Before you go on, right-click your OneDrive folder and choose \"Always keep on this " +
            "device\" for anything you need offline, and wait for it to download.",
    };

    public static TweakDefinition RemoveStore { get; } = new(
        Id: "debloat.remove-store",
        Section: Sections.DebloatAndPrivacy,
        Name: "Remove the Microsoft Store",
        WhatItDoes:
            "Uninstalls the Microsoft Store app for your account. The Apps section keeps working, because it only " +
            "uses winget's community source.",
        Evidence:
            "Remove-AppxPackage removes the Store like any other Store package. Only worth doing if you never use " +
            "Store apps: nothing gets faster.",
        Sources:
        [
            RemoveAppxDocs,
            new("Microsoft: How to reinstall Microsoft Store",
                "https://learn.microsoft.com/en-us/answers/questions/4028551/how-to-reinstall-microsoft-store-after-being-unins"),
        ],
        Tier: EvidenceTier.Proven,
        Undo:
            "Puts the Store back with Microsoft's own command, wsreset -i, which downloads it again and needs an " +
            "internet connection. Store apps then update normally again.",
        Steps: [new AppRemovalStep(MicrosoftStore)])
    {
        Reversibility = Reversibility.NeedsDownload,
        ApplyWarning =
            "Without the Store, Game Pass and the Xbox app stop working: they can't install or update games. Store " +
            "apps (including Xbox, Game Bar and Windows' own apps) stop getting updates, and you can't install " +
            "anything from the Store.",
    };

    // ---- Curated preinstalled apps (story 53) ----

    /// <summary>
    /// Preinstalled apps that are safe to remove for one account. Deliberately left out: Edge and WebView2 (never
    /// removed), the Xbox apps and Game Bar (Game Pass and X3D core parking need them), App Installer (winget), and
    /// Windows' own tools (Photos, Calculator, Notepad, Terminal, Paint, Snipping Tool).
    /// </summary>
    public static IReadOnlyList<RemovableApp> Preinstalled { get; } =
    [
        new("Clipchamp", "Clipchamp.Clipchamp", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9P1J8S7CCWWT"),
        new("Microsoft News", "Microsoft.BingNews", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9WZDNCRFHVFW"),
        new("Weather", "Microsoft.BingWeather", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9WZDNCRFJ3Q2"),
        new("Solitaire & Casual Games", "Microsoft.MicrosoftSolitaireCollection", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9WZDNCRFHWD2"),
        new("Microsoft Teams (free)", "MSTeams", AppKind.Appx, ReinstallFrom.MicrosoftStore, "XP8BT8DW290MPQ"),
        new("Microsoft 365 Copilot (Office hub)", "Microsoft.MicrosoftOfficeHub", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9WZDNCRD29V9"),
        new("Outlook (new)", "Microsoft.OutlookForWindows", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9NRX63209R7B"),
        new("Microsoft To Do", "Microsoft.Todos", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9NBLGGH5R558"),
        new("Power Automate", "Microsoft.PowerAutomateDesktop", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9NFTCH6J7FHV"),
        new("Microsoft Family Safety", "MicrosoftCorporationII.MicrosoftFamily", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9PDJDJS743XF"),
        new("Maps", "Microsoft.WindowsMaps", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9WZDNCRDTBVB"),
        new("Get Help", "Microsoft.GetHelp", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9PKDZBMV1H3T"),
        new("Feedback Hub", "Microsoft.WindowsFeedbackHub", AppKind.Appx, ReinstallFrom.MicrosoftStore, "9NBLGGH4R32N"),
    ];

    public static IReadOnlyList<TweakDefinition> RemovePreinstalled { get; } = Preinstalled.Select(Removal).ToList();

    private static TweakDefinition Removal(RemovableApp app) => new(
        Id: $"debloat.remove-app.{app.PackageName.ToLowerInvariant()}",
        Section: Sections.DebloatAndPrivacy,
        Name: $"Remove {app.Name}",
        WhatItDoes:
            $"Uninstalls {app.Name} for your account. Other accounts on this PC keep it. Windows may install it " +
            "again for new accounts unless you also stop preinstalled apps coming back for new accounts.",
        Evidence:
            $"{app.Name} is a Store app Windows preinstalls; Remove-AppxPackage uninstalls it for one account. A " +
            "decluttering choice, not a performance one.",
        Sources: [RemoveAppxDocs],
        Tier: EvidenceTier.Proven,
        Undo: StoreUndo,
        Steps: [new AppRemovalStep(app)])
    {
        Reversibility = Reversibility.NeedsDownload,
    };

    // ---- Recall (story 47) ----

    public static TweakDefinition RemoveRecall { get; } = new(
        Id: "privacy.remove-recall",
        Section: Sections.DebloatAndPrivacy,
        Name: "Remove Recall",
        WhatItDoes:
            "Turns off the Recall Windows feature on this Copilot+ PC, so Recall can't run or save snapshots at all, " +
            "for every account. Its files stay on disk so it can be turned back on without a download. Turning off " +
            "Recall snapshots by policy is a separate, lighter switch.",
        Evidence:
            "Microsoft documents Recall as an optional Windows feature that can be removed from Turn Windows features " +
            "on or off, or with DISM / Disable-WindowsOptionalFeature.",
        Sources:
        [
            new("Microsoft: Manage Recall",
                "https://learn.microsoft.com/en-us/windows/client-management/manage-recall"),
            new("Microsoft: Disable-WindowsOptionalFeature",
                "https://learn.microsoft.com/en-us/powershell/module/dism/disable-windowsoptionalfeature"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: "Turns the Recall feature back on. Takes effect after a restart.",
        Steps:
        [
            new OptionalFeatureStep("Recall", Enabled: false,
                Missing: "Recall only comes with Copilot+ PCs, and this PC doesn't have it."),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
        // Copilot+ PCs only: on every other PC there is nothing to remove, so it isn't shown.
        Applicability = new(MinBuild: 26100, Hide: true),
    };

    // ---- Future accounts (story 54) ----

    public static TweakDefinition DeprovisionPreinstalled { get; } = new(
        Id: "debloat.deprovision-preinstalled",
        Section: Sections.DebloatAndPrivacy,
        Name: "Stop these apps being installed for new accounts",
        WhatItDoes:
            "Removes Copilot and the preinstalled apps listed above from the copy Windows installs for every new " +
            "account (provisioned packages), so accounts made later don't get them. Accounts that already exist, " +
            "including yours, are not changed: remove the apps from your account with their own switches.",
        Evidence:
            "Remove-AppxProvisionedPackage removes a package from the Windows image so new user profiles don't get " +
            "it; it doesn't uninstall it from existing users. Re-adding it needs the original package file and its " +
            "licence, which home PCs don't have.",
        Sources:
        [
            new("Microsoft: Remove-AppxProvisionedPackage",
                "https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage"),
            new("Microsoft: Preinstalled apps (provisioning)",
                "https://learn.microsoft.com/en-us/windows/msix/desktop/deploy-preinstalled-apps"),
        ],
        Tier: EvidenceTier.Proven,
        Undo:
            "Can't be undone on this PC. Each new account can still install the apps from the Microsoft Store; a " +
            "Windows repair install or a feature update brings them back for new accounts.",
        Steps: [new AppDeprovisionStep(Copilot), .. Preinstalled.Select(app => new AppDeprovisionStep(app))])
    {
        Reversibility = Reversibility.Destructive,
        DestructiveWarning =
            "Windows keeps no copy to put these apps back for new accounts. Undo can't restore them; only a Windows " +
            "repair install, a feature update or installing each app from the Store per account brings them back.",
    };

    public static IReadOnlyList<TweakDefinition> All { get; } =
        [RemoveCopilot, RemoveOneDrive, RemoveStore, .. RemovePreinstalled, RemoveRecall, DeprovisionPreinstalled];
}
