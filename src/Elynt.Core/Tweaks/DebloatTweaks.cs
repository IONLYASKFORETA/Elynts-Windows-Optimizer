using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>
/// Debloat &amp; Privacy settings (spec user stories 44, 45, 47, 48, 50, 52). Every one is a reversible registry
/// change: nothing here removes an app, so Edge, the Xbox apps and Game Bar stay installed. App removal is a
/// separate group (ticket 9).
/// </summary>
public static class DebloatTweaks
{
    private const string DataCollectionPolicy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string EdgePolicy = @"SOFTWARE\Policies\Microsoft\Edge";

    private const string SignOutNote =
        " Windows may only pick this up after you sign out and back in, or after Explorer restarts.";

    private const string EdgeManagedNote =
        " Edge stays installed, so WebView2 apps keep working. Because these are Edge policies, Edge's settings page " +
        "shows \"Your browser is managed by your organization\" while this is on.";

    private const string PutsBack =
        "Puts back exactly the values that were there before; values that didn't exist are deleted again.";

    private static readonly Source DiagnosticDataDocs = new(
        "Microsoft: Configure Windows diagnostic data in your organization",
        "https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization");

    private static readonly Source ExperiencePolicies = new(
        "Microsoft: Policy CSP - Experience",
        "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience");

    private static readonly Source WinUtilGuides = new(
        "WinUtil: tweak guides and code reference", "https://winutil.christitus.com/guides/");

    private static readonly Source EdgePolicies = new(
        "Microsoft: Microsoft Edge browser policy documentation",
        "https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies");

    private static RegistryValueStep CurrentUser(string key, string name, uint value) =>
        new(new(RegistryRoot.CurrentUser, key, name), RegistryValue.DWord(value));

    private static RegistryValueStep LocalMachine(string key, string name, uint value) =>
        new(new(RegistryRoot.LocalMachine, key, name), RegistryValue.DWord(value));

    // ---- Telemetry ----

    public static TweakDefinition DiagnosticDataMinimum { get; } = new(
        Id: "privacy.diagnostic-data-minimum",
        Section: Sections.DebloatAndPrivacy,
        Name: "Send the least diagnostic data Windows allows",
        WhatItDoes:
            "Sets the diagnostic data policy to its lowest level. Enterprise and Education honour \"off\" (Security " +
            "level); Home and Pro treat it as \"Required\", the least those editions send. Optional diagnostic data " +
            "is no longer sent.",
        Evidence:
            "Microsoft documents the AllowTelemetry policy and its levels, including that level 0 only applies to " +
            "Enterprise, Education and IoT and is treated as Required elsewhere. A privacy setting, not a performance one.",
        Sources:
        [
            DiagnosticDataDocs,
            new("Microsoft: Policy CSP - System (AllowTelemetry)",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps: [LocalMachine(DataCollectionPolicy, "AllowTelemetry", 0)]);

    public static TweakDefinition DiagTrackServiceOff { get; } = new(
        Id: "privacy.diagtrack-service-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off the telemetry service (DiagTrack)",
        WhatItDoes:
            "Stops the Connected User Experiences and Telemetry service (DiagTrack) from starting, so it no longer " +
            "uploads diagnostic data. The service stays installed. Windows Update, Defender and games are not affected.",
        Evidence:
            "DiagTrack is the service that collects and sends Windows diagnostic data, as Microsoft's diagnostic data " +
            "documentation describes. Turning it off is a privacy change, not a performance one.",
        Sources: [DiagnosticDataDocs],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back the service's exact previous start type (normally Automatic). Takes effect after a restart.",
        Steps: [LocalMachine(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", (uint)ServiceStart.Disabled)])
    {
        // The service manager only reads a changed start type at the next boot.
        Risks = RiskFlags.NeedsReboot,
    };

    public static TweakDefinition FeedbackPromptsOff { get; } = new(
        Id: "privacy.feedback-prompts-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off feedback prompts",
        WhatItDoes:
            "Stops Windows asking for feedback (\"How likely are you to recommend Windows…\"). You can still open " +
            "Feedback Hub yourself.",
        Evidence:
            "Microsoft documents the DoNotShowFeedbackNotifications policy, which stops feedback prompts; the " +
            "Settings > Privacy > Diagnostics & feedback > Feedback frequency switch writes the per-user value.",
        Sources:
        [
            new("Microsoft: Policy CSP - Experience (DoNotShowFeedbackNotifications)",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            LocalMachine(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1),
            CurrentUser(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
        ]);

    public static TweakDefinition TailoredExperiencesOff { get; } = new(
        Id: "privacy.tailored-experiences-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off tailored experiences",
        WhatItDoes:
            "Stops Microsoft using your diagnostic data to personalise tips, ads and recommendations in Windows.",
        Evidence:
            "Microsoft documents the \"Do not use diagnostic data for tailored experiences\" policy. A privacy setting.",
        Sources: [ExperiencePolicies],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            CurrentUser(@"Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1),
            CurrentUser(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
        ]);

    // ---- Ads and suggestions ----

    public static TweakDefinition StartRecommendationsOff { get; } = new(
        Id: "debloat.start-recommendations-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Start menu recommendations",
        WhatItDoes:
            "Stops the Start menu recommending tips, shortcuts, new apps and promoted apps. Your recent files still " +
            "show unless you turn those off in Settings." + SignOutNote,
        Evidence:
            "This is the Settings > Personalization > Start switch \"Show recommendations for tips, shortcuts, new apps " +
            "and more\", plus the content-delivery setting behind suggested apps in Start.",
        Sources:
        [
            new("Microsoft: Policy CSP - Start",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-start"),
            WinUtilGuides,
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            CurrentUser(ExplorerAdvanced, "Start_IrisRecommendations", 0),
            CurrentUser(ContentDelivery, "SubscribedContent-338388Enabled", 0),
        ]);

    public static TweakDefinition LockScreenTipsOff { get; } = new(
        Id: "debloat.lock-screen-tips-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off lock screen tips and fun facts",
        WhatItDoes:
            "Stops Windows putting tips, tricks, fun facts and promotions on the lock screen. Your lock screen picture " +
            "stays as it is.",
        Evidence:
            "This is the Settings > Personalization > Lock screen switch \"Get fun facts, tips, tricks and more on " +
            "your lock screen\".",
        Sources:
        [
            ExperiencePolicies,
            WinUtilGuides,
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            CurrentUser(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0),
            CurrentUser(ContentDelivery, "SubscribedContent-338387Enabled", 0),
        ]);

    public static TweakDefinition SettingsSuggestionsOff { get; } = new(
        Id: "debloat.settings-suggestions-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off suggestions in the Settings app",
        WhatItDoes: "Stops the Settings app showing suggested content and offers.",
        Evidence:
            "This is the Settings > Privacy & security > General switch \"Show me suggested content in the Settings app\".",
        Sources:
        [
            WinUtilGuides,
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            CurrentUser(ContentDelivery, "SubscribedContent-338393Enabled", 0),
            CurrentUser(ContentDelivery, "SubscribedContent-353694Enabled", 0),
            CurrentUser(ContentDelivery, "SubscribedContent-353696Enabled", 0),
        ]);

    public static TweakDefinition ExplorerPromosOff { get; } = new(
        Id: "debloat.explorer-promos-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off File Explorer promotions",
        WhatItDoes:
            "Stops File Explorer showing sync provider notifications, the banners that advertise OneDrive and " +
            "Microsoft 365." + SignOutNote,
        Evidence:
            "This is File Explorer's Folder Options > View switch \"Show sync provider notifications\".",
        Sources: [WinUtilGuides],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps: [CurrentUser(ExplorerAdvanced, "ShowSyncProviderNotifications", 0)]);

    public static TweakDefinition BingInStartSearchOff { get; } = new(
        Id: "debloat.bing-in-start-search-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Bing web results in Start search",
        WhatItDoes:
            "Start search only searches your PC: no Bing web results and no web search suggestions as you type." +
            SignOutNote,
        Evidence:
            "Microsoft documents the DisableSearchBoxSuggestions policy (\"Turn off display of recent search entries " +
            "in the File Explorer search box\"), which also turns off web results in Windows search.",
        Sources:
        [
            new("Microsoft: Policy CSP - ADMX_WindowsExplorer (DisableSearchBoxSuggestions)",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps: [CurrentUser(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)]);

    // ---- Widgets and Recall ----

    public static TweakDefinition WidgetsOff { get; } = new(
        Id: "debloat.widgets-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Widgets",
        WhatItDoes:
            "Removes the Widgets button and news feed from the taskbar for every account on this PC. The Widgets " +
            "app is not uninstalled." + SignOutNote,
        Evidence: "Microsoft documents the AllowNewsAndInterests policy, which turns Widgets off.",
        Sources:
        [
            new("Microsoft: Policy CSP - NewsAndInterests",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps: [LocalMachine(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)]);

    public static TweakDefinition RecallOff { get; } = new(
        Id: "privacy.recall-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Recall snapshots",
        WhatItDoes:
            "Stops Recall saving snapshots of your screen, by policy, for every account on this PC. Recall itself " +
            "stays installed; removing it from a Copilot+ PC is a separate, opt-in tweak. On PCs without Recall the " +
            "policy simply has nothing to do.",
        Evidence:
            "Microsoft documents the DisableAIDataAnalysis policy (\"Turn off saving snapshots for use with Recall\") " +
            "for both the device and the user.",
        Sources:
        [
            new("Microsoft: Manage Recall",
                "https://learn.microsoft.com/en-us/windows/client-management/manage-recall"),
            new("Microsoft: Policy CSP - WindowsAI",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            LocalMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
            CurrentUser(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
        ])
    {
        // Recall arrived with Windows 11 24H2.
        Applicability = new(MinBuild: 26100),
    };

    // ---- Edge ----

    public static TweakDefinition EdgeBackgroundOff { get; } = new(
        Id: "debloat.edge-background-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Stop Edge running in the background",
        WhatItDoes:
            "Turns off Edge's startup boost (Edge preloading itself when you sign in) and stops Edge running in the " +
            "background after you close it." + EdgeManagedNote,
        Evidence:
            "Microsoft documents the StartupBoostEnabled and BackgroundModeEnabled Edge policies. Frees a little " +
            "memory and sign-in time; Edge's first launch is a little slower.",
        Sources: [EdgePolicies],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            LocalMachine(EdgePolicy, "StartupBoostEnabled", 0),
            LocalMachine(EdgePolicy, "BackgroundModeEnabled", 0),
        ]);

    public static TweakDefinition EdgeNagsOff { get; } = new(
        Id: "debloat.edge-nags-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Edge prompts and promotions",
        WhatItDoes:
            "Skips Edge's first-run setup, stops its \"make Edge your default browser\" campaigns, feature " +
            "recommendations and promotional tabs." + EdgeManagedNote,
        Evidence:
            "Microsoft documents the HideFirstRunExperience, DefaultBrowserSettingsCampaignEnabled, " +
            "ShowRecommendationsEnabled and PromotionalTabsEnabled Edge policies.",
        Sources: [EdgePolicies],
        Tier: EvidenceTier.Proven,
        Undo: PutsBack,
        Steps:
        [
            LocalMachine(EdgePolicy, "HideFirstRunExperience", 1),
            LocalMachine(EdgePolicy, "DefaultBrowserSettingsCampaignEnabled", 0),
            LocalMachine(EdgePolicy, "ShowRecommendationsEnabled", 0),
            LocalMachine(EdgePolicy, "PromotionalTabsEnabled", 0),
        ]);

    // ---- Game DVR ----

    public static TweakDefinition GameDvrBackgroundCaptureOff { get; } = new(
        Id: "debloat.game-dvr-background-capture-off",
        Section: Sections.DebloatAndPrivacy,
        Name: "Turn off Game DVR background recording",
        WhatItDoes:
            "Turns off \"Record what happened\", the background recording that keeps the last few minutes of play. " +
            "Game Bar and the Xbox apps stay installed and working, so you can still record on demand, and X3D core " +
            "parking (which needs Game Bar) keeps working.",
        Evidence:
            "Background recording continuously encodes video while you play. Microsoft's own troubleshooting fixes a " +
            "performance problem by turning it off. The benefit depends on your hardware, and no benchmark shows " +
            "anything extra from removing Game Bar entirely.",
        Sources:
        [
            new("Microsoft: Performance is affected when Teams is remembered as a game",
                "https://learn.microsoft.com/en-us/microsoftteams/troubleshoot/teams-on-windows/performance-affected-when-teams-remembered-as-game"),
        ],
        Tier: EvidenceTier.Situational,
        Undo: PutsBack,
        Steps:
        [
            CurrentUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
            CurrentUser(@"System\GameConfigStore", "GameDVR_Enabled", 0),
        ]);

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        DiagnosticDataMinimum, DiagTrackServiceOff, FeedbackPromptsOff, TailoredExperiencesOff,
        StartRecommendationsOff, LockScreenTipsOff, SettingsSuggestionsOff, ExplorerPromosOff, BingInStartSearchOff,
        WidgetsOff, RecallOff, EdgeBackgroundOff, EdgeNagsOff, GameDvrBackgroundCaptureOff,
    ];
}
