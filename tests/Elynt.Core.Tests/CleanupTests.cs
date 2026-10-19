using Elynt.Core.Cleanup;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Cleanup section, driven through the Engine's cleanup commands against a fake PC.</summary>
public class CleanupTests
{
    private const string User = "S-1-5-21-test";
    private const string Local = FakeCleanupSystem.Profile + @"\AppData\Local";

    private readonly FakeCleanupSystem _pc = new();
    private readonly FakeRegistry _registry = new();
    private readonly InMemoryCleanupScheduleStore _schedules = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    private Engine NewEngine(Safety.SafetyNet? safetyNet = null) => new(
        Catalogue.Default,
        new SystemAdapters(_registry, new FakeSystemInfo(), Cleanup: _pc),
        new ChangeJournal(new InMemoryJournalStore()),
        User,
        safetyNet,
        clock: _clock,
        cleanupSchedules: _schedules);

    private CleanupCategoryView Category(CleanupScan scan, string id) => scan.Categories.Single(c => c.Id == id);

    private Task<CleanupScan> ScanAsync() => NewEngine().ScanCleanupAsync(cancellation: TestContext.Current.CancellationToken);

    /// <summary>An engine that has scanned, as the Cleanup page does before Clean. The scan's measuring isn't kept as an action.</summary>
    private async Task<Engine> ScannedEngineAsync(Engine? engine = null)
    {
        engine ??= NewEngine();
        await engine.ScanCleanupAsync(cancellation: TestContext.Current.CancellationToken);
        _pc.Actions.Clear();
        return engine;
    }

    [Fact]
    public async Task A_scan_reports_the_size_of_every_category_and_deletes_nothing()
    {
        _pc.AddFile(@"C:\Windows\Temp\setup.log", 1_000);
        _pc.AddFile(@"C:\Windows\Temp\old\a.tmp", 500);
        _pc.AddFile($@"{Local}\Temp\b.tmp", 2_000);
        _pc.AddFile(@"C:\Windows\SoftwareDistribution\Download\abc\update.cab", 40_000);

        var scan = await ScanAsync();

        var temp = Category(scan, CleanupCategoryIds.TempFiles);
        Assert.Equal(3_500, temp.SizeBytes);
        Assert.Equal(3, temp.FileCount);
        Assert.Equal(40_000, Category(scan, CleanupCategoryIds.WindowsUpdateCache).SizeBytes);
        Assert.Equal(0, Category(scan, CleanupCategoryIds.Thumbnails).SizeBytes);
        Assert.All(scan.Categories.Where(c => c.Id != CleanupCategoryIds.ComponentStore), c => Assert.NotNull(c.SizeBytes));
        Assert.True(_pc.FileExists(@"C:\Windows\Temp\setup.log"));
        Assert.True(_pc.FileExists($@"{Local}\Temp\b.tmp"));
        Assert.DoesNotContain(_pc.Actions, a => a.StartsWith("clear") || a.StartsWith("delete") || a.Contains(".exe"));
    }

    [Fact]
    public async Task Safe_caches_come_ticked_while_shader_caches_and_destructive_extras_do_not()
    {
        var scan = await ScanAsync();

        foreach (var id in new[] { CleanupCategoryIds.TempFiles, CleanupCategoryIds.WindowsUpdateCache, CleanupCategoryIds.DeliveryOptimization,
                     CleanupCategoryIds.Thumbnails, CleanupCategoryIds.CrashDumps, CleanupCategoryIds.ErrorReports, CleanupCategoryIds.ChromeCache })
            Assert.True(Category(scan, id).Ticked, id);

        var shaders = Category(scan, CleanupCategoryIds.ShaderCaches);
        Assert.False(shaders.Ticked);
        Assert.Contains("stutter", shaders.Warning);

        foreach (var id in new[] { CleanupCategoryIds.WindowsOld, CleanupCategoryIds.ComponentStore, CleanupCategoryIds.OldDrivers, CleanupCategoryIds.RecycleBin })
        {
            var destructive = Category(scan, id);
            Assert.False(destructive.Ticked, id);
            Assert.True(destructive.NeedsConfirmation, id);
            Assert.False(string.IsNullOrWhiteSpace(destructive.Warning));
        }
    }

    [Fact]
    public async Task A_browser_cache_is_measured_but_skipped_while_that_browser_is_open()
    {
        _pc.AddFile($@"{Local}\Google\Chrome\User Data\Default\Cache\Cache_Data\f_000001", 7_000);
        _pc.AddFile($@"{Local}\Google\Chrome\User Data\Profile 1\Cache\Cache_Data\f_000002", 3_000);
        _pc.AddFile($@"{Local}\Google\Chrome\User Data\Default\History", 9_999);
        _pc.Running.Add("chrome");

        var chrome = Category(await ScanAsync(), CleanupCategoryIds.ChromeCache);
        Assert.Equal(10_000, chrome.SizeBytes);
        Assert.False(chrome.Ticked);
        Assert.Contains("Chrome is open", chrome.SkippedReason);

        var report = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.ChromeCache], false, cancellation: TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(report.Categories).Cleaned);
        Assert.True(_pc.FileExists($@"{Local}\Google\Chrome\User Data\Default\Cache\Cache_Data\f_000001"));

        _pc.Running.Clear();
        report = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.ChromeCache], false, cancellation: TestContext.Current.CancellationToken);
        Assert.True(report.Succeeded, report.Message);
        Assert.Equal(10_000, report.BytesFreed);
        Assert.False(_pc.FileExists($@"{Local}\Google\Chrome\User Data\Profile 1\Cache\Cache_Data\f_000002"));
        Assert.True(_pc.FileExists($@"{Local}\Google\Chrome\User Data\Default\History"));
    }

    public static TheoryData<string> UserFolders() => new(
        "Documents", "Downloads", "Desktop", "Pictures", "Music", "Videos", "Saved Games", "OneDrive", "OneDrive - Contoso", "Favorites");

    [Theory]
    [MemberData(nameof(UserFolders))]
    public async Task User_folders_are_never_scanned_or_cleaned_even_when_every_file_on_the_PC_is_in_them(string folder)
    {
        // Fill the user folder and the profile with files, and nothing anywhere else.
        _pc.AddFile($@"{FakeCleanupSystem.Profile}\{folder}\Temp\notes.tmp", 1_000);
        _pc.AddFile($@"{FakeCleanupSystem.Profile}\{folder}\thumbcache_1.db", 1_000);
        _pc.AddFile($@"{FakeCleanupSystem.Profile}\{folder}\My Games\save.dat", 1_000);
        _pc.AddFile($@"{FakeCleanupSystem.Profile}\NTUSER.DAT", 1_000);

        var scan = await ScanAsync();
        await (await ScannedEngineAsync()).CleanAsync(scan.Categories.Select(c => c.Id).ToList(), confirmDestructive: true,
            cancellation: TestContext.Current.CancellationToken);

        Assert.All(scan.Categories, c => Assert.DoesNotContain(c.Locations, l => l.Contains(folder, StringComparison.OrdinalIgnoreCase)));
        Assert.All(scan.Categories.Where(c => c.SizeBytes is not null), c => Assert.Equal(0, c.SizeBytes));
        Assert.DoesNotContain(_pc.Actions, a => a.Contains(folder, StringComparison.OrdinalIgnoreCase) || a.EndsWith(@"\aden"));
        Assert.True(_pc.FileExists($@"{FakeCleanupSystem.Profile}\{folder}\My Games\save.dat"));
        Assert.True(_pc.FileExists($@"{FakeCleanupSystem.Profile}\NTUSER.DAT"));
    }

    [Fact]
    public void Every_cleanup_location_resolves_inside_AppData_or_outside_the_user_profile()
    {
        var resolved = CleanupCatalogue.Default.Categories
            .SelectMany(c => c.Sources.OfType<FolderSource>())
            .Select(f => $@"{FakeCleanupSystem.RootPath(f.Root, User)}\{f.RelativePath}".TrimEnd('\\'))
            .ToList();

        Assert.All(resolved, path =>
        {
            if (path.StartsWith(FakeCleanupSystem.Profile + @"\", StringComparison.OrdinalIgnoreCase))
                Assert.StartsWith(FakeCleanupSystem.Profile + @"\AppData\", path, StringComparison.OrdinalIgnoreCase);
            Assert.Null(HardRules.Violation(new FileTarget(path)));
        });
    }

    [Theory]
    [InlineData(@"C:\Users\aden\Documents")]
    [InlineData(@"C:\Users\aden\Downloads\Temp")]
    [InlineData(@"C:\Users\aden\Desktop")]
    [InlineData(@"C:\Users\aden\Saved Games\Shaders")]
    [InlineData(@"C:\Users\aden\Contacts")]
    [InlineData(@"C:\Users\aden")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows\Temp\..\..\Users\aden\Documents")]
    [InlineData(@"Temp")]
    public async Task A_PC_that_resolves_a_location_into_a_user_folder_still_gets_nothing_scanned_or_deleted_there(string folder)
    {
        _pc.AddFile($@"{folder}\precious.docx", 5_000);
        _pc.Resolve(CleanupRoot.Windows, "Temp", folder);
        _pc.Resolve(CleanupRoot.UserLocalAppData, "Temp", folder);

        var temp = Category(await ScanAsync(), CleanupCategoryIds.TempFiles);
        var report = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.TempFiles], false, cancellation: TestContext.Current.CancellationToken);

        Assert.Empty(temp.Locations);
        Assert.Equal(0, temp.SizeBytes);
        Assert.Equal(0, report.BytesFreed);
        Assert.DoesNotContain(_pc.Actions, a => a.EndsWith(folder, StringComparison.OrdinalIgnoreCase));
        Assert.True(_pc.FileExists($@"{folder}\precious.docx"));
    }

    [Fact]
    public async Task Cleaning_deletes_the_ticked_categories_leaves_files_in_use_and_reports_what_it_freed()
    {
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);
        _pc.AddFile(@"C:\Windows\Temp\locked.tmp", 2_000, inUse: true);
        _pc.AddFile($@"{Local}\Microsoft\Windows\Explorer\thumbcache_256.db", 4_000);
        _pc.AddFile($@"{Local}\Microsoft\Windows\Explorer\iconcache_256.db", 8_000);
        _pc.AddFile($@"{Local}\NVIDIA\DXCache\shader.bin", 16_000);
        var progress = new List<CleanupProgress>();

        var report = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.TempFiles, CleanupCategoryIds.Thumbnails], false, new Collect(progress),
            TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded, report.Message);
        Assert.Equal(5_000, report.BytesFreed);
        Assert.Equal(1, report.Categories.Single(c => c.CategoryId == CleanupCategoryIds.TempFiles).InUse);
        Assert.False(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
        Assert.True(_pc.FileExists(@"C:\Windows\Temp\locked.tmp"));
        Assert.True(_pc.FileExists($@"{Local}\Microsoft\Windows\Explorer\iconcache_256.db"));
        Assert.True(_pc.FileExists($@"{Local}\NVIDIA\DXCache\shader.bin"));
        Assert.Equal(
            [(CleanupPhase.Cleaning, CleanupCategoryIds.TempFiles, 1, 2), (CleanupPhase.Cleaning, CleanupCategoryIds.Thumbnails, 2, 2)],
            progress.Select(p => (p.Phase, p.CategoryId, p.Index, p.Count)));
    }

    [Fact]
    public async Task A_scan_streams_which_category_it_is_measuring()
    {
        var progress = new List<CleanupProgress>();

        var scan = await NewEngine().ScanCleanupAsync(new Collect(progress), TestContext.Current.CancellationToken);

        Assert.Equal(scan.Categories.Select(c => c.Id), progress.Select(p => p.CategoryId));
        Assert.All(progress, p => Assert.Equal(CleanupPhase.Scanning, p.Phase));
        Assert.Equal(Enumerable.Range(1, scan.Categories.Count), progress.Select(p => p.Index));
    }

    [Theory]
    [InlineData(CleanupCategoryIds.WindowsOld)]
    [InlineData(CleanupCategoryIds.ComponentStore)]
    [InlineData(CleanupCategoryIds.OldDrivers)]
    [InlineData(CleanupCategoryIds.RecycleBin)]
    public async Task A_destructive_category_needs_a_separate_confirmation_and_without_it_nothing_is_deleted(string id)
    {
        SeedDestructiveExtras();
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);

        var refused = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.TempFiles, id], confirmDestructive: false,
            cancellation: TestContext.Current.CancellationToken);

        Assert.False(refused.Succeeded);
        Assert.Contains("can't be undone", refused.Message);
        Assert.Contains(CleanupCatalogue.Default.Categories.Single(c => c.Id == id).Warning!, refused.Message);
        Assert.Empty(refused.Categories);
        Assert.Empty(_pc.Actions);
        Assert.True(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));

        var confirmed = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.TempFiles, id], confirmDestructive: true,
            cancellation: TestContext.Current.CancellationToken);
        Assert.True(confirmed.Succeeded, confirmed.Message);
        Assert.False(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
    }

    [Fact]
    public async Task Destructive_extras_measure_and_clean_what_they_say()
    {
        SeedDestructiveExtras();

        var scan = await ScanAsync();
        Assert.Equal(300_000, Category(scan, CleanupCategoryIds.WindowsOld).SizeBytes);
        Assert.Equal(1_234, Category(scan, CleanupCategoryIds.RecycleBin).SizeBytes);
        Assert.Null(Category(scan, CleanupCategoryIds.ComponentStore).SizeBytes);
        var drivers = Category(scan, CleanupCategoryIds.OldDrivers);
        Assert.Equal(60_000, drivers.SizeBytes);
        Assert.Equal(2, drivers.FileCount);
        Assert.Contains(drivers.Locations, l => l.StartsWith("oem10.inf"));
        Assert.Contains(drivers.Locations, l => l.StartsWith("oem11.inf"));

        var report = await (await ScannedEngineAsync()).CleanAsync(
            [CleanupCategoryIds.WindowsOld, CleanupCategoryIds.ComponentStore, CleanupCategoryIds.OldDrivers, CleanupCategoryIds.RecycleBin],
            confirmDestructive: true, cancellation: TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded, report.Message);
        Assert.False(_pc.FolderExists(@"C:\Windows.old"));
        Assert.False(_pc.FileExists($@"C:\$Recycle.Bin\{User}\$RABC123.txt"));
        Assert.True(_pc.FileExists(@"C:\$Recycle.Bin\S-1-5-21-other\$Rsomeone-else.txt"));
        Assert.Contains("dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase", _pc.Actions);
        // Only versions a newer one replaced, never the newest and never one a device uses.
        Assert.Equal(["oem12.inf", "oem13.inf", "oem14.inf"], _pc.DriverPackages.Select(p => p.PublishedName).Order());
    }

    [Fact]
    public async Task ResetBase_failing_is_reported_and_the_other_categories_still_clean()
    {
        _pc.RunExitCode = unchecked((int)0x800f0806);
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);

        var report = await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.ComponentStore, CleanupCategoryIds.TempFiles], true,
            cancellation: TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.False(report.Categories.Single(c => c.CategoryId == CleanupCategoryIds.ComponentStore).Cleaned);
        Assert.True(report.Categories.Single(c => c.CategoryId == CleanupCategoryIds.TempFiles).Cleaned);
        Assert.False(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
    }

    [Fact]
    public async Task Unknown_or_empty_requests_delete_nothing()
    {
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);

        Assert.False((await (await ScannedEngineAsync()).CleanAsync([CleanupCategoryIds.TempFiles, "cleanup.documents"], true,
            cancellation: TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await (await ScannedEngineAsync()).CleanAsync([], true, cancellation: TestContext.Current.CancellationToken)).Succeeded);
        Assert.Empty(_pc.Actions);
    }

    [Fact]
    public async Task Weekly_cleaning_is_off_by_default_and_does_nothing_until_turned_on()
    {
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);
        var engine = NewEngine();

        Assert.False(engine.GetCleanupSchedule().WeeklyEnabled);
        Assert.Null(await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken));
        Assert.True(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
    }

    [Fact]
    public async Task Weekly_cleaning_only_touches_safe_categories()
    {
        SeedDestructiveExtras();
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);
        _pc.AddFile($@"{Local}\Microsoft\Edge\User Data\Default\Cache\data_1", 2_000);
        _pc.AddFile($@"{Local}\Mozilla\Firefox\Profiles\abc.default\cache2\entries\X", 3_000);
        _pc.AddFile($@"{Local}\NVIDIA\DXCache\shader.bin", 16_000);
        _pc.AddFile($@"{Local}\D3DSCache\game\cache.idx", 16_000);
        _pc.Running.Add("firefox");
        var engine = NewEngine();
        engine.SetWeeklyCleanup(true);

        var report = await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        var safe = CleanupCatalogue.Default.Categories.Where(c => c.Kind == CleanupKind.Safe).Select(c => c.Id);
        Assert.Equal(safe, report.Categories.Select(c => c.CategoryId));
        Assert.False(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
        Assert.False(_pc.FileExists($@"{Local}\Microsoft\Edge\User Data\Default\Cache\data_1"));
        Assert.True(_pc.FileExists($@"{Local}\Mozilla\Firefox\Profiles\abc.default\cache2\entries\X"));
        Assert.True(_pc.FileExists($@"{Local}\NVIDIA\DXCache\shader.bin"));
        Assert.True(_pc.FileExists($@"{Local}\D3DSCache\game\cache.idx"));
        Assert.True(_pc.FolderExists(@"C:\Windows.old"));
        Assert.True(_pc.FileExists($@"C:\$Recycle.Bin\{User}\$RABC123.txt"));
        Assert.Equal(5, _pc.DriverPackages.Count);
        Assert.DoesNotContain(_pc.Actions, a => a.Contains("dism", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Weekly_cleaning_runs_once_a_week_and_stops_when_turned_off()
    {
        var engine = NewEngine();
        engine.SetWeeklyCleanup(true);

        Assert.NotNull(await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken));
        Assert.Equal(_clock.GetUtcNow(), engine.GetCleanupSchedule().LastRun);

        _clock.Advance(TimeSpan.FromDays(6));
        Assert.Null(await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken));

        engine.SetWeeklyCleanup(false);
        _clock.Advance(TimeSpan.FromDays(30));
        Assert.Null(await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_schedules.UsersWithWeeklyCleaning());
    }

    [Fact]
    public async Task The_weekly_choice_survives_a_service_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elynt-cleanup-{Guid.NewGuid():N}.json");
        try
        {
            Engine EngineOn(ICleanupScheduleStore store) => new(Catalogue.Default, new SystemAdapters(_registry, new FakeSystemInfo(), Cleanup: _pc),
                new ChangeJournal(new InMemoryJournalStore()), User, clock: _clock, cleanupSchedules: store);
            var before = EngineOn(new JsonFileCleanupScheduleStore(path));
            before.SetWeeklyCleanup(true);
            await before.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken);

            var restarted = new JsonFileCleanupScheduleStore(path);
            Assert.Equal([User], restarted.UsersWithWeeklyCleaning());
            Assert.Equal(new CleanupSchedule(true, _clock.GetUtcNow()), EngineOn(restarted).GetCleanupSchedule());
            Assert.Equal(CleanupSchedule.Off, restarted.Load("S-1-5-21-someone-else"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_hibernation_file_is_a_normal_reversible_tweak_that_explains_Fast_Startup()
    {
        var hibernation = NewEngine().ListTweaks().Single(t => t.Id == CleanupTweaks.HibernationOff.Id);

        Assert.Equal(Sections.Cleanup, hibernation.Section);
        Assert.Equal(Reversibility.Reversible, hibernation.Reversibility);
        Assert.True(hibernation.Risks.HasFlag(RiskFlags.NeedsReboot));
        Assert.Contains("Fast Startup", hibernation.WhatItDoes);
        Assert.False(hibernation.ProfileEligible);
    }

    [Fact]
    public async Task Cleanup_leaves_Storage_Sense_and_the_registry_alone()
    {
        SeedDestructiveExtras();
        var engine = await ScannedEngineAsync();

        await engine.CleanAsync(CleanupCatalogue.Default.Categories.Select(c => c.Id).ToList(), true, cancellation: TestContext.Current.CancellationToken);
        engine.SetWeeklyCleanup(true);
        await engine.RunWeeklyCleanupIfDueAsync(TestContext.Current.CancellationToken);

        Assert.True(_registry.IsEmpty);
        Assert.DoesNotContain(Catalogue.Default.Tweaks.SelectMany(t => t.Steps).OfType<Registry.RegistryValueStep>(),
            step => step.Location.KeyPath.Contains("StorageSense", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cleaning_without_a_scan_first_is_refused_and_nothing_is_deleted()
    {
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);

        var report = await NewEngine().CleanAsync([CleanupCategoryIds.TempFiles], false, cancellation: TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Contains("Scan first", report.Message);
        Assert.True(_pc.FileExists(@"C:\Windows\Temp\a.tmp"));
        Assert.DoesNotContain(_pc.Actions, a => a.StartsWith("clear"));
    }

    [Fact]
    public async Task Destructive_cleaning_attempts_a_restore_point_and_backs_up_the_drivers_first()
    {
        SeedDestructiveExtras();
        var restorePoints = new RecordingRestorePoints();
        var backups = new RecordingStoreBackups();
        var engine = await ScannedEngineAsync(NewEngine(new Safety.SafetyNet(restorePoints, backups, _clock)));

        var report = await engine.CleanAsync([CleanupCategoryIds.OldDrivers, CleanupCategoryIds.WindowsOld], true,
            cancellation: TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded, report.Message);
        Assert.Single(restorePoints.Attempts);
        Assert.Equal([Safety.BackupStore.Drivers], backups.Exports);
    }

    [Fact]
    public async Task Old_drivers_are_kept_when_the_drivers_cannot_be_backed_up()
    {
        SeedDestructiveExtras();
        var backups = new RecordingStoreBackups { Fail = true };
        var engine = await ScannedEngineAsync(NewEngine(new Safety.SafetyNet(new RecordingRestorePoints(), backups, _clock)));

        var report = await engine.CleanAsync([CleanupCategoryIds.OldDrivers], true, cancellation: TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Contains("Nothing was deleted", report.Message);
        Assert.Equal(5, _pc.DriverPackages.Count);
    }

    [Fact]
    public async Task Safe_cleaning_needs_no_restore_point()
    {
        _pc.AddFile(@"C:\Windows\Temp\a.tmp", 1_000);
        var restorePoints = new RecordingRestorePoints();
        var engine = await ScannedEngineAsync(NewEngine(new Safety.SafetyNet(restorePoints, new RecordingStoreBackups(), _clock)));

        Assert.True((await engine.CleanAsync([CleanupCategoryIds.TempFiles], false, cancellation: TestContext.Current.CancellationToken)).Succeeded);
        Assert.Empty(restorePoints.Attempts);
    }

    private sealed class RecordingRestorePoints : Safety.IRestorePoints
    {
        public List<string> Attempts { get; } = [];

        public Safety.RestorePointResult TryCreate(string description)
        {
            Attempts.Add(description);
            return Safety.RestorePointResult.Created();
        }
    }

    private sealed class RecordingStoreBackups : Safety.IStoreBackups
    {
        public List<Safety.BackupStore> Exports { get; } = [];
        public bool Fail { get; set; }

        public string Export(Safety.BackupStore store)
        {
            if (Fail)
                throw new IOException("pnputil /export-driver failed.");
            Exports.Add(store);
            return store.ToString();
        }
    }

    private void SeedDestructiveExtras()
    {
        _pc.AddFile(@"C:\Windows.old\Windows\System32\kernel32.dll", 100_000);
        _pc.AddFile(@"C:\Windows.old\Users\aden\Documents\old.docx", 200_000);
        _pc.AddFile($@"C:\$Recycle.Bin\{User}\$RABC123.txt", 1_234);
        _pc.AddFile(@"C:\$Recycle.Bin\S-1-5-21-other\$Rsomeone-else.txt", 9_999);
        _pc.DriverPackages.AddRange(
        [
            Driver("oem10.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.6094", 40_000),
            Driver("oem11.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.6590", 20_000),
            Driver("oem12.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.8088", 50_000),
            Driver("oem13.inf", "rt640x64.inf", "Realtek", "Net", "10.60.0.0", 5_000, inUse: true),
            Driver("oem14.inf", "rt640x64.inf", "Realtek", "Net", "10.70.0.0", 6_000),
        ]);
    }

    private static DriverPackage Driver(string published, string original, string provider, string className, string version, long size, bool inUse = false) =>
        new(published, original, provider, className, Version.Parse(version), new DateOnly(2025, 1, 1), size, inUse);

    private sealed class Collect(List<CleanupProgress> into) : IProgress<CleanupProgress>
    {
        public void Report(CleanupProgress value) => into.Add(value);
    }
}
