namespace Elynt.Core.Cleanup;

public static class CleanupCategoryIds
{
    public const string TempFiles = "cleanup.temp";
    public const string WindowsUpdateCache = "cleanup.windows-update";
    public const string DeliveryOptimization = "cleanup.delivery-optimization";
    public const string Thumbnails = "cleanup.thumbnails";
    public const string CrashDumps = "cleanup.crash-dumps";
    public const string ErrorReports = "cleanup.error-reports";
    public const string ChromeCache = "cleanup.browser.chrome";
    public const string EdgeCache = "cleanup.browser.edge";
    public const string FirefoxCache = "cleanup.browser.firefox";
    public const string BraveCache = "cleanup.browser.brave";
    public const string OperaCache = "cleanup.browser.opera";
    public const string ShaderCaches = "cleanup.shader-caches";
    public const string WindowsOld = "cleanup.windows-old";
    public const string ComponentStore = "cleanup.component-store";
    public const string OldDrivers = "cleanup.old-drivers";
    public const string RecycleBin = "cleanup.recycle-bin";
}

/// <summary>
/// Every category cleanup offers (spec user stories 83–91). Locations are fixed folders under fixed roots; nothing
/// here can point at the user's own folders. Storage Sense is never touched.
/// </summary>
public sealed class CleanupCatalogue
{
    private readonly Dictionary<string, CleanupCategory> _byId;

    public CleanupCatalogue(IEnumerable<CleanupCategory> categories)
    {
        Categories = categories.ToList();
        foreach (var category in Categories)
        {
            if (category.Kind != CleanupKind.Safe && string.IsNullOrWhiteSpace(category.Warning))
                throw new ArgumentException($"Cleanup category '{category.Id}' must warn before it is cleaned.");
            if (category.Sources.OfType<FolderSource>().Any(f => f.RelativePath.Split('\\', '/').Any(part => part is ".." or ".")))
                throw new ArgumentException($"Cleanup category '{category.Id}' must stay inside its folders.");
        }
        _byId = Categories.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CleanupCategory> Categories { get; }

    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CleanupCategory? category) =>
        _byId.TryGetValue(id, out category);

    private const string BrowserCacheText =
        "Deletes the browser's cached web pages and images. Your history, passwords, cookies and tabs stay. Sites load " +
        "a little slower the first time. Skipped while the browser is open.";

    private static CleanupCategory Browser(string id, string name, string process, params string[] cacheFolders) => new(
        id,
        $"{name} cache",
        BrowserCacheText,
        CleanupKind.Safe,
        cacheFolders.Select(f => (CleanupSource)new FolderSource(CleanupRoot.UserLocalAppData, f)).ToList(),
        BlockingProcess: process,
        BlockingProgramName: name);

    public static CleanupCatalogue Default { get; } = new(
    [
        new(CleanupCategoryIds.TempFiles,
            "Temporary files",
            "Deletes leftovers from installers and apps in Windows' and your temp folders. Files in use are left alone.",
            CleanupKind.Safe,
            [new FolderSource(CleanupRoot.Windows, "Temp"), new FolderSource(CleanupRoot.UserLocalAppData, "Temp")]),
        new(CleanupCategoryIds.WindowsUpdateCache,
            "Windows Update downloads",
            "Deletes update files Windows already downloaded and installed. Windows downloads again anything it still needs.",
            CleanupKind.Safe,
            [new FolderSource(CleanupRoot.Windows, @"SoftwareDistribution\Download")]),
        new(CleanupCategoryIds.DeliveryOptimization,
            "Delivery Optimization cache",
            "Deletes the copies of updates Windows keeps to share with other PCs. Your own updates aren't affected.",
            CleanupKind.Safe,
            [new FolderSource(CleanupRoot.Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")]),
        new(CleanupCategoryIds.Thumbnails,
            "Thumbnail cache",
            "Deletes the picture previews File Explorer keeps. Explorer makes them again when you open a folder.",
            CleanupKind.Safe,
            [new FolderSource(CleanupRoot.UserLocalAppData, @"Microsoft\Windows\Explorer", "thumbcache_*.db", IncludeSubfolders: false)]),
        new(CleanupCategoryIds.CrashDumps,
            "Crash dumps",
            "Deletes memory dumps saved when Windows or an app crashed. Only needed if someone is debugging that crash.",
            CleanupKind.Safe,
            [
                new FolderSource(CleanupRoot.Windows, "Minidump"),
                new FolderSource(CleanupRoot.Windows, "", "MEMORY.DMP", IncludeSubfolders: false),
                new FolderSource(CleanupRoot.Windows, "LiveKernelReports", "*.dmp"),
                new FolderSource(CleanupRoot.UserLocalAppData, "CrashDumps"),
            ]),
        new(CleanupCategoryIds.ErrorReports,
            "Windows error reports",
            "Deletes problem reports already sent or waiting to be sent to Microsoft.",
            CleanupKind.Safe,
            [
                new FolderSource(CleanupRoot.ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
                new FolderSource(CleanupRoot.ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"Microsoft\Windows\WER\ReportArchive"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"Microsoft\Windows\WER\ReportQueue"),
            ]),
        Browser(CleanupCategoryIds.ChromeCache, "Chrome", "chrome",
            @"Google\Chrome\User Data\*\Cache", @"Google\Chrome\User Data\*\Code Cache"),
        Browser(CleanupCategoryIds.EdgeCache, "Edge", "msedge",
            @"Microsoft\Edge\User Data\*\Cache", @"Microsoft\Edge\User Data\*\Code Cache"),
        Browser(CleanupCategoryIds.FirefoxCache, "Firefox", "firefox",
            @"Mozilla\Firefox\Profiles\*\cache2"),
        Browser(CleanupCategoryIds.BraveCache, "Brave", "brave",
            @"BraveSoftware\Brave-Browser\User Data\*\Cache", @"BraveSoftware\Brave-Browser\User Data\*\Code Cache"),
        Browser(CleanupCategoryIds.OperaCache, "Opera", "opera",
            @"Opera Software\Opera Stable\Cache", @"Opera Software\Opera GX Stable\Cache"),
        new(CleanupCategoryIds.ShaderCaches,
            "Shader caches",
            "Deletes the compiled shaders your graphics driver and DirectX keep for games (NVIDIA, AMD, Intel and DirectX " +
            "caches). Only worth it after a driver problem or to free space.",
            CleanupKind.ShaderCache,
            [
                new FolderSource(CleanupRoot.UserLocalAppData, "D3DSCache"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"NVIDIA\DXCache"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"NVIDIA\GLCache"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"AMD\DxCache"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"AMD\DxcCache"),
                new FolderSource(CleanupRoot.UserLocalAppData, @"AMD\VkCache"),
                new FolderSource(CleanupRoot.UserLocalLow, @"Intel\ShaderCache"),
            ],
            Warning:
                "Games stutter the first time you play them afterwards, while the driver compiles their shaders again. " +
                "Some games also rebuild shaders at launch, which can take minutes."),
        new(CleanupCategoryIds.WindowsOld,
            "Previous Windows installation (Windows.old)",
            "Deletes the copy of your previous Windows that a feature update keeps for 10 days so you can go back.",
            CleanupKind.Destructive,
            [new FolderSource(CleanupRoot.SystemDrive, "Windows.old", RemoveFolder: true)],
            Warning:
                "You can no longer go back to your previous version of Windows. Any files still left in Windows.old " +
                "(including its old Users folder) are deleted for good."),
        new(CleanupCategoryIds.ComponentStore,
            "Superseded Windows updates (ResetBase)",
            "Runs DISM /StartComponentCleanup /ResetBase, which deletes the old versions of every Windows component that " +
            "an update replaced. Can take a long time. Windows can only tell how much it frees by doing it.",
            CleanupKind.Destructive,
            [new ComponentStoreSource()],
            Warning: "Updates installed so far can no longer be uninstalled."),
        new(CleanupCategoryIds.OldDrivers,
            "Old driver packages",
            "Deletes older versions of drivers from Windows' driver store when a newer version of the same driver is " +
            "installed. Drivers a device is using are never removed.",
            CleanupKind.Destructive,
            [new OldDriverPackagesSource()],
            Warning: "You can no longer roll a device back to these older drivers from Device Manager."),
        new(CleanupCategoryIds.RecycleBin,
            "Recycle Bin",
            "Empties your Recycle Bin on the Windows drive.",
            CleanupKind.Destructive,
            [new FolderSource(CleanupRoot.UserRecycleBin, "")],
            Warning: "Everything in your Recycle Bin is deleted for good and can't be restored."),
    ]);
}
