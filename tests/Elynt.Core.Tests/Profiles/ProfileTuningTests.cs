using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.Profiles;
using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests.Profiles;

/// <summary>
/// GPU and CPU tuning inside a per-game profile, through the Engine: applied when the game starts, put back with the
/// rest of the profile, crash-safe through the tuning safety store, and checked against the game's anti-cheat.
/// </summary>
public sealed class ProfileTuningTests : IDisposable
{
    private const string User = "S-1-5-21-test";
    private const string Game = @"C:\Games\Cyberpunk 2077\Cyberpunk2077.exe";
    private const string Gpu = FakeGpuTuning.DeviceId;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTuningStore _tuningStore = new();
    private readonly InMemoryCpuProfileStore _cpuProfiles = new();
    private readonly InMemoryProfileStore _profiles = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly FakeGameSources _pc = new();
    private readonly InMemoryGameStore _games = new();
    private readonly FakeProcesses _processes = new();
    private readonly FakeTimerResolution _timer = new();
    private readonly FakeGpuTuning _gpu = new();
    private readonly FakeCpu _cpu = FakeCpu.Raphael7800X3D();
    private GpuTuning _gpuService = null!;
    private CpuTuning _cpuService = null!;

    public ProfileTuningTests() => StartServices();

    public void Dispose()
    {
        _gpuService.Dispose();
        _cpuService.Dispose();
    }

    private static readonly GpuProfileTuning Undervolt =
        new(Gpu, new Dictionary<string, int> { [GpuControls.Voltage] = 1100, [GpuControls.MaxClock] = 2600 });

    private static readonly CpuProfileTuning CoolCpu = new(new CpuTune(CurveOptimizer: -10, PptWatts: 120));

    [Fact]
    public void GPU_tuning_in_a_profile_is_applied_when_the_game_starts_and_put_back_when_it_stops()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt] });

        var started = engine.StartProfile(Game, 4242);

        Assert.True(started.Succeeded, started.Message);
        Assert.Equal(1100, _gpu.Value(GpuControls.Voltage));
        Assert.Equal(2600, _gpu.Value(GpuControls.MaxClock));

        var stopped = engine.StopProfile(Game);

        Assert.True(stopped.Succeeded, stopped.Message);
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void CPU_tuning_in_a_profile_is_applied_when_the_game_starts_and_put_back_when_it_stops()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { CpuTuning = CoolCpu });

        var started = engine.StartProfile(Game, 4242);

        Assert.True(started.Succeeded, started.Message);
        Assert.Equal(-10, _cpu.CurveOptimizer);
        Assert.Equal(120, _cpu.Ppt);

        var stopped = engine.StopProfile(Game);

        Assert.True(stopped.Succeeded, stopped.Message);
        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void After_the_game_the_kept_GPU_and_CPU_tunes_come_back_not_the_defaults()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt], CpuTuning = CoolCpu });
        Assert.True(engine.SetGpuTuning(Gpu, new Dictionary<string, int> { [GpuControls.PowerLimit] = 10 }).Succeeded);
        Assert.True(engine.ConfirmGpuTuning(Gpu).Succeeded);
        Assert.True(engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default).Succeeded);
        Assert.True(engine.ConfirmCpuTuning().Succeeded);

        engine.StartProfile(Game, 4242);
        var stopped = engine.StopProfile(Game);

        Assert.True(stopped.Succeeded, stopped.Message);
        Assert.Equal(10, _gpu.Value(GpuControls.PowerLimit));
        Assert.Equal(1150, _gpu.Value(GpuControls.Voltage));
        Assert.Equal(2500, _gpu.Value(GpuControls.MaxClock));
        Assert.Equal(-5, _cpu.CurveOptimizer);
        Assert.Equal(162, _cpu.Ppt);
    }

    [Fact]
    public void A_change_still_waiting_to_be_kept_goes_back_when_the_game_starts_so_it_never_outlives_the_game()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt], CpuTuning = CoolCpu });
        engine.SetGpuTuning(Gpu, new Dictionary<string, int> { [GpuControls.PowerLimit] = 10 });
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default);

        engine.StartProfile(Game, 4242);
        engine.StopProfile(Game);

        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.True(_cpu.AtBios);
        Assert.Null(engine.GetGpuTuning().Gpus.Single().RevertsAt);
        Assert.Null(engine.GetCpuTuning(false).RevertsAt);
    }

    [Fact]
    public void A_GPU_value_outside_the_SDK_range_is_refused_when_the_profile_is_saved()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game)
        {
            GpuTuning = [new(Gpu, new Dictionary<string, int> { [GpuControls.MaxClock] = 3500 })],
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Max GPU clock", result.Message);
        Assert.Empty(NewEngine().ListProfiles());
    }

    [Fact]
    public void A_GPU_this_PC_does_not_have_is_refused_when_the_profile_is_saved()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game)
        {
            GpuTuning = [new("nvidia:3", new Dictionary<string, int> { [GpuControls.PowerLimit] = 5 })],
        });

        Assert.False(result.Succeeded);
        Assert.Contains("nvidia:3", result.Message);
    }

    [Theory]
    [InlineData(-20, false)]
    [InlineData(5, true)]
    public void A_CPU_tune_outside_the_Engine_limits_is_refused_when_the_profile_is_saved(int curveOptimizer, bool advanced)
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game)
        {
            CpuTuning = new(new CpuTune(CurveOptimizer: curveOptimizer), Advanced: advanced),
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Curve Optimizer", result.Message);
        Assert.Empty(NewEngine().ListProfiles());
    }

    [Fact]
    public void A_CPU_power_limit_above_the_BIOS_value_is_refused_when_the_profile_is_saved()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game) { CpuTuning = new(new CpuTune(PptWatts: 200)) });

        Assert.False(result.Succeeded);
        Assert.Contains("PPT", result.Message);
    }

    [Fact]
    public void Saving_a_profile_with_tuning_changes_nothing_on_the_hardware()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game) { GpuTuning = [Undervolt], CpuTuning = CoolCpu });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([Undervolt], Assert.Single(NewEngine().ListProfiles()).GpuTuning);
        Assert.Equal(CoolCpu, Assert.Single(NewEngine().ListProfiles()).CpuTuning);
        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.False(_cpu.Touched);
    }

    [Fact]
    public void A_clean_service_stop_mid_game_puts_the_GPU_and_CPU_back_so_nothing_outlives_the_game()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt], CpuTuning = CoolCpu });
        engine.StartProfile(Game, 4242);

        _cpuService.Shutdown();
        _gpuService.Shutdown();
        StartServices();

        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void A_crash_mid_game_puts_the_GPU_on_defaults_at_the_next_start_and_never_re_applies_the_profile_tune()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt] });
        engine.StartProfile(Game, 4242);

        // A crash: the services never stop cleanly.
        _gpuService.Dispose();
        _cpuService.Dispose();
        StartServices();

        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.Contains("didn't shut down cleanly", NewEngine().GetGpuTuning().Notice);
    }

    [Fact]
    public void While_a_game_profile_holds_the_GPU_and_CPU_the_Overclocking_page_cannot_change_them()
    {
        var engine = EngineWithProfile(new GameProfile(Game) { GpuTuning = [Undervolt], CpuTuning = CoolCpu });
        engine.StartProfile(Game, 4242);

        var gpu = engine.SetGpuTuning(Gpu, new Dictionary<string, int> { [GpuControls.PowerLimit] = 10 });
        var cpu = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -3), CpuTuningOptions.Default);

        Assert.False(gpu.Succeeded);
        Assert.Contains("profile", gpu.Message);
        Assert.False(cpu.Succeeded);
        Assert.Contains("profile", cpu.Message);
        Assert.Equal(1100, _gpu.Value(GpuControls.Voltage));
        Assert.Equal(-10, _cpu.CurveOptimizer);
    }

    // ---- Anti-cheat warnings while editing ----

    private const string Cs2 = @"D:\Games\Counter-Strike 2\cs2.exe";
    private const string Siege = @"D:\Games\Rainbow Six\RainbowSix.exe";

    [Fact]
    public void Editing_CPU_tuning_for_a_FACEIT_game_warns_that_FACEIT_blocks_PawnIO()
    {
        var engine = WithGame(Cs2);

        var check = engine.CheckProfile(new GameProfile(Cs2) { CpuTuning = CoolCpu });

        Assert.True(check.Succeeded, check.Message);
        var warning = Assert.Single(check.Warnings);
        Assert.Contains("PawnIO", warning);
        Assert.Contains("FACEIT", warning);
        Assert.Empty(NewEngine().ListProfiles());
    }

    [Fact]
    public void Saving_still_works_with_the_warning_and_returns_it()
    {
        var saved = WithGame(Cs2).SaveProfile(new GameProfile(Cs2) { CpuTuning = CoolCpu });

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Contains(saved.Warnings, w => w.Contains("FACEIT"));
    }

    [Fact]
    public void Editing_CPU_tuning_for_a_BattlEye_game_warns_that_an_unstable_tune_can_get_you_kicked()
    {
        var check = WithGame(Siege).CheckProfile(new GameProfile(Siege) { CpuTuning = CoolCpu });

        Assert.Contains(check.Warnings, w => w.Contains("BattlEye") && w.Contains("kick"));
    }

    [Fact]
    public void GPU_tuning_through_the_vendor_driver_has_no_anti_cheat_warning()
    {
        var check = WithGame(Cs2).CheckProfile(new GameProfile(Cs2) { GpuTuning = [Undervolt] });

        Assert.True(check.Succeeded, check.Message);
        Assert.Empty(check.Warnings);
    }

    [Fact]
    public void Checking_a_profile_that_cannot_be_saved_says_why()
    {
        var check = NewEngine().CheckProfile(new GameProfile(Game) { CpuTuning = new(new CpuTune(CurveOptimizer: -40)) });

        Assert.False(check.Succeeded);
        Assert.Contains("Curve Optimizer", check.Message);
    }

    // ---- Helpers ----

    private Engine WithGame(string exePath)
    {
        _pc.File(exePath);
        var engine = NewEngine();
        Assert.True(engine.AddGame(exePath).Succeeded);
        return engine;
    }

    private Engine EngineWithProfile(GameProfile profile)
    {
        var engine = NewEngine();
        var saved = engine.SaveProfile(profile);
        Assert.True(saved.Succeeded, saved.Message);
        return engine;
    }

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Games: _pc) { Processes = _processes, Timer = _timer },
        _journal,
        User,
        clock: _clock,
        gameStore: _games,
        gpuTuning: _gpuService,
        profiles: _profiles,
        cpuTuning: _cpuService);

    private void StartServices()
    {
        var safety = new TuningSafety(_tuningStore);
        _gpuService = new GpuTuning([_gpu], safety, _clock);
        _cpuService = new CpuTuning(_cpu, safety, _cpuProfiles, _cpu, _cpu, _clock);
        var start = safety.BeginSession();
        _gpuService.RecoverAtStartup(start);
        _cpuService.RecoverAtStartup(start);
    }
}
