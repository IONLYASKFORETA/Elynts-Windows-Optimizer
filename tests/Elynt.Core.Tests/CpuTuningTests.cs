using Elynt.Core.Bios;
using Elynt.Core.Journal;
using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests;

/// <summary>
/// CPU tuning through the Engine: negative-only Curve Optimizer and lower-only power limits behind a per-codename SMU
/// allowlist, 60 s confirm-or-revert, a stress test before auto-apply, delayed re-apply and the two-strike crash rule.
/// </summary>
public sealed class CpuTuningTests : IDisposable
{
    private readonly InMemoryTuningStore _tuningStore = new();
    private readonly InMemoryCpuProfileStore _profiles = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private FakeCpu _cpu = FakeCpu.Raphael7800X3D();
    private CpuTuning? _service;
    private FakeCpu? _serviceCpu;

    public void Dispose() => _service?.Dispose();

    // ---- What the Engine offers ----

    [Fact]
    public void A_Ryzen_7000_offers_negative_only_Curve_Optimizer_capped_at_minus_15_and_lower_only_limits()
    {
        var cpu = NewEngine().GetCpuTuning(advanced: false);

        Assert.True(cpu.Available);
        Assert.Equal(CpuMaker.Amd, cpu.Maker);
        var co = Control(cpu, CpuControls.CurveOptimizer);
        Assert.Equal((-15, 0, 0, 0), (co.Min, co.Max, co.Default, co.Current));
        var ppt = Control(cpu, CpuControls.Ppt);
        Assert.Equal((81, 162, 162, 162), (ppt.Min, ppt.Max, ppt.Default, ppt.Current));
        Assert.Equal(120, Control(cpu, CpuControls.Tdc).Max);
        Assert.Equal(180, Control(cpu, CpuControls.Edc).Max);
        Assert.DoesNotContain(cpu.Controls, c => c.Id is CpuControls.Pl1 or CpuControls.Pl2);
        Assert.False(cpu.CurveOptimizerUnofficial);
        Assert.False(_cpu.Touched);
    }

    [Fact]
    public void Advanced_lets_Curve_Optimizer_go_to_minus_30_and_never_further()
    {
        var co = Control(NewEngine().GetCpuTuning(advanced: true), CpuControls.CurveOptimizer);

        Assert.Equal((-30, 0), (co.Min, co.Max));
    }

    [Fact]
    public void Ryzen_9000_X3D_is_on_the_reviewed_list()
    {
        _cpu = FakeCpu.GraniteRidge9800X3D();

        var cpu = NewEngine().GetCpuTuning(advanced: false);

        Assert.True(cpu.Available);
        Assert.Contains(cpu.Controls, c => c.Id == CpuControls.CurveOptimizer);
    }

    [Fact]
    public void Ryzen_3000_is_off_the_reviewed_list_so_it_cannot_be_tuned_and_gets_no_SMU_message()
    {
        _cpu = FakeCpu.Matisse3700X();
        var engine = NewEngine();

        var cpu = engine.GetCpuTuning(advanced: true);
        var set = engine.SetCpuTuning(new CpuTune(PptWatts: 70), CpuTuningOptions.Default);

        Assert.False(cpu.Available);
        Assert.False(set.Succeeded);
        Assert.Empty(_cpu.SmuMessages);
    }

    [Fact]
    public void The_5800X3D_offers_Curve_Optimizer_only_as_unofficial()
    {
        _cpu = FakeCpu.Vermeer5800X3D();

        var cpu = NewEngine().GetCpuTuning(advanced: false);

        Assert.True(cpu.CurveOptimizerUnofficial);
        Assert.Contains(cpu.Warnings, w => w.Contains("unofficial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_CPU_whose_codename_is_not_on_the_SMU_allowlist_cannot_be_tuned_and_gets_no_SMU_message()
    {
        _cpu = FakeCpu.Phoenix8700G();
        var engine = NewEngine();

        var cpu = engine.GetCpuTuning(advanced: true);
        var set = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5, PptWatts: 60), CpuTuningOptions.Default);

        Assert.False(cpu.Available);
        Assert.Empty(cpu.Controls);
        Assert.False(set.Succeeded);
        Assert.Empty(_cpu.SmuMessages);
    }

    [Fact]
    public void Without_PawnIO_nothing_is_offered_and_the_CPU_is_never_touched()
    {
        _cpu.Unavailable = "PawnIO isn't installed.";
        var engine = NewEngine();

        var cpu = engine.GetCpuTuning(advanced: false);
        var set = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default);

        Assert.False(cpu.Available);
        Assert.Empty(cpu.Controls);
        Assert.Contains("PawnIO", cpu.Notice);
        Assert.False(set.Succeeded);
        Assert.False(_cpu.Touched);
    }

    [Fact]
    public void Intel_offers_PL1_and_PL2_capped_at_the_BIOS_values()
    {
        _cpu = FakeCpu.Intel14900K(pl1: 253, pl2: 253);

        var cpu = NewEngine().GetCpuTuning(advanced: false);

        Assert.True(cpu.Available);
        var pl1 = Control(cpu, CpuControls.Pl1);
        Assert.Equal((35, 253, 253), (pl1.Min, pl1.Max, pl1.Current));
        Assert.Equal(253, Control(cpu, CpuControls.Pl2).Max);
        Assert.DoesNotContain(cpu.Controls, c => c.Id == CpuControls.CurveOptimizer);
        Assert.Empty(_cpu.MsrWrites);
    }

    [Fact]
    public void Intel_power_limits_are_hidden_when_the_BIOS_locked_them()
    {
        _cpu = FakeCpu.Intel14900K(locked: true);
        var engine = NewEngine();

        var cpu = engine.GetCpuTuning(advanced: false);
        var set = engine.SetCpuTuning(new CpuTune(Pl1Watts: 125), CpuTuningOptions.Default);

        Assert.Empty(cpu.Controls);
        Assert.Contains("locked", cpu.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.False(set.Succeeded);
        Assert.Empty(_cpu.MsrWrites);
    }

    [Fact]
    public void A_14900K_gets_Intel_Default_Settings_and_a_warning_when_its_microcode_is_older_than_0x12B()
    {
        _cpu = FakeCpu.Intel14900K(microcode: 0x129);

        var cpu = NewEngine().GetCpuTuning(advanced: false);

        Assert.Equal(new CpuTune(Pl1Watts: 125, Pl2Watts: 253), cpu.IntelDefaults);
        Assert.Contains(cpu.Warnings, w => w.Contains("0x12B"));
    }

    [Fact]
    public void Current_microcode_and_older_generations_get_no_microcode_warning()
    {
        _cpu = FakeCpu.Intel14900K(microcode: 0x12B);
        Assert.DoesNotContain(NewEngine().GetCpuTuning(false).Warnings, w => w.Contains("0x12B"));

        _cpu = FakeCpu.Intel12700K();
        var older = NewEngine().GetCpuTuning(false);
        Assert.DoesNotContain(older.Warnings, w => w.Contains("0x12B"));
        Assert.Null(older.IntelDefaults);
    }

    [Fact]
    public void Intel_Default_Settings_never_go_above_the_BIOS_values()
    {
        _cpu = FakeCpu.Intel14900K(pl1: 100, pl2: 200);

        Assert.Equal(new CpuTune(Pl1Watts: 100, Pl2Watts: 200), NewEngine().GetCpuTuning(false).IntelDefaults);
    }

    [Fact]
    public void With_PawnIO_installed_the_FACEIT_warning_is_shown_but_tuning_still_works()
    {
        var pawnIo = new FakePawnIo { Version = "2.0.1" };
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), new FakeSystemInfo())
            {
                PawnIo = new LowLevel.PawnIoAdapters(pawnIo, pawnIo, Path.GetTempPath(), new FakeDownloader(), new FakeSignatureVerifier()),
            },
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-test",
            clock: _clock,
            cpuTuning: StartService());

        var cpu = engine.GetCpuTuning(false);
        var set = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default);

        Assert.Contains(cpu.Warnings, w => w.Contains("FACEIT"));
        Assert.True(set.Succeeded, set.Message);
        Assert.Contains(set.Cpu.Warnings, w => w.Contains("FACEIT"));
    }

    // ---- Limits, enforced in the Engine ----

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, true)]
    [InlineData(-16, false)]
    [InlineData(-31, true)]
    [InlineData(-50, true)]
    public void Curve_Optimizer_outside_the_cap_or_positive_is_refused_and_nothing_is_sent(int counts, bool advanced)
    {
        var result = NewEngine().SetCpuTuning(new CpuTune(CurveOptimizer: counts), new CpuTuningOptions(Advanced: advanced));

        Assert.False(result.Succeeded);
        Assert.Empty(_cpu.SmuMessages);
        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void Advanced_allows_minus_30()
    {
        var result = NewEngine().SetCpuTuning(new CpuTune(CurveOptimizer: -30), new CpuTuningOptions(Advanced: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(-30, _cpu.CurveOptimizer);
    }

    [Theory]
    [InlineData(163, null, null)]
    [InlineData(80, null, null)]
    [InlineData(null, 121, null)]
    [InlineData(null, null, 181)]
    [InlineData(null, null, 89)]
    public void AMD_limits_can_only_go_down_from_the_BIOS_value_and_not_below_half_of_it(int? ppt, int? tdc, int? edc)
    {
        var result = NewEngine().SetCpuTuning(new CpuTune(PptWatts: ppt, TdcAmps: tdc, EdcAmps: edc), CpuTuningOptions.Default);

        Assert.False(result.Succeeded);
        Assert.Empty(_cpu.SmuMessages);
    }

    [Fact]
    public void A_Ryzen_7000_tune_goes_through_its_allowlisted_SMU_messages_only()
    {
        var result = NewEngine().SetCpuTuning(new CpuTune(CurveOptimizer: -10, PptWatts: 120, TdcAmps: 90, EdcAmps: 150), CpuTuningOptions.Default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((-10, 120.0, 90.0, 150.0), (_cpu.CurveOptimizer, _cpu.Ppt, _cpu.Tdc, _cpu.Edc));
        Assert.All(_cpu.SmuMessages, m => Assert.Contains(m.Message, new uint[] { 0x07, 0x56, 0x57, 0x58 }));
        Assert.Equal(new CpuTune(CurveOptimizer: -10, PptWatts: 120, TdcAmps: 90, EdcAmps: 150), result.Cpu.Active);
        Assert.Equal(-10, Control(result.Cpu, CpuControls.CurveOptimizer).Current);
    }

    [Fact]
    public void A_Ryzen_5000_tune_uses_the_Zen_3_messages()
    {
        _cpu = FakeCpu.Vermeer5900X();

        var result = NewEngine().SetCpuTuning(new CpuTune(CurveOptimizer: -12, PptWatts: 100), CpuTuningOptions.Default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((-12, 100.0), (_cpu.CurveOptimizer, _cpu.Ppt));
        Assert.All(_cpu.SmuMessages, m => Assert.Contains(m.Message, new uint[] { 0x0B, 0x53, 0x54, 0x55 }));
    }

    [Fact]
    public void Lowering_only_PPT_leaves_the_BIOS_Curve_Optimizer_alone()
    {
        NewEngine().SetCpuTuning(new CpuTune(PptWatts: 120), CpuTuningOptions.Default);

        Assert.Equal([0x56u], _cpu.SmuMessages.Select(m => m.Message));
    }

    [Fact]
    public void The_5800X3D_needs_the_unofficial_tick_for_Curve_Optimizer()
    {
        _cpu = FakeCpu.Vermeer5800X3D();
        var engine = NewEngine();

        var refused = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), CpuTuningOptions.Default);
        Assert.False(refused.Succeeded);
        Assert.Empty(_cpu.SmuMessages);

        var allowed = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), new CpuTuningOptions(Unofficial: true));
        Assert.True(allowed.Succeeded, allowed.Message);
        Assert.Equal(-10, _cpu.CurveOptimizer);
    }

    [Fact]
    public void Intel_settings_on_an_AMD_CPU_are_refused()
    {
        Assert.False(NewEngine().SetCpuTuning(new CpuTune(Pl1Watts: 100), CpuTuningOptions.Default).Succeeded);
        Assert.False(_cpu.Touched);
    }

    [Fact]
    public void Intel_PL1_goes_down_through_MSR_0x610_and_keeps_PL2()
    {
        _cpu = FakeCpu.Intel14900K(pl1: 253, pl2: 253);

        var result = NewEngine().SetCpuTuning(new CpuTune(Pl1Watts: 125), CpuTuningOptions.Default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((125, 253), (_cpu.Pl1, _cpu.Pl2));
        Assert.All(_cpu.MsrWrites, w => Assert.Equal(0x610u, w.Register));
        Assert.Empty(_cpu.SmuMessages);
    }

    [Fact]
    public void Intel_Default_Settings_apply_as_an_ordinary_tune()
    {
        _cpu = FakeCpu.Intel14900K();
        var engine = NewEngine();

        var result = engine.SetCpuTuning(engine.GetCpuTuning(false).IntelDefaults!, CpuTuningOptions.Default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((125, 253), (_cpu.Pl1, _cpu.Pl2));
    }

    [Theory]
    [InlineData(254, null)]
    [InlineData(null, 300)]
    [InlineData(34, null)]
    [InlineData(200, 150)]
    public void Intel_limits_can_only_go_down_not_below_35_W_and_PL1_stays_under_PL2(int? pl1, int? pl2)
    {
        _cpu = FakeCpu.Intel14900K(pl1: 253, pl2: 253);

        var result = NewEngine().SetCpuTuning(new CpuTune(Pl1Watts: pl1, Pl2Watts: pl2), CpuTuningOptions.Default);

        Assert.False(result.Succeeded);
        Assert.Empty(_cpu.MsrWrites);
    }

    [Fact]
    public void AMD_settings_on_an_Intel_CPU_are_refused()
    {
        _cpu = FakeCpu.Intel14900K();

        Assert.False(NewEngine().SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default).Succeeded);
        Assert.Empty(_cpu.MsrWrites);
    }

    [Fact]
    public void An_empty_tune_is_refused()
    {
        Assert.False(NewEngine().SetCpuTuning(new CpuTune(), CpuTuningOptions.Default).Succeeded);
        Assert.False(_cpu.Touched);
    }

    // ---- Confirm-or-revert ----

    [Fact]
    public void A_change_goes_back_to_the_BIOS_settings_after_60_seconds_unless_kept()
    {
        var engine = NewEngine();

        var result = engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10, PptWatts: 120), CpuTuningOptions.Default);

        Assert.Equal(_clock.GetUtcNow().AddSeconds(60), result.Cpu.RevertsAt);
        _clock.Advance(TimeSpan.FromSeconds(59.9));
        Assert.Equal(-10, _cpu.CurveOptimizer);

        _clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True(_cpu.AtBios);
        var cpu = engine.GetCpuTuning(false);
        Assert.Null(cpu.Active);
        Assert.Null(cpu.Kept);
        Assert.Null(cpu.RevertsAt);
        Assert.NotNull(cpu.Notice);
    }

    [Fact]
    public void A_kept_change_stays()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), CpuTuningOptions.Default);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var kept = engine.ConfirmCpuTuning();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(kept.Succeeded, kept.Message);
        Assert.Equal(-10, _cpu.CurveOptimizer);
        Assert.Equal(new CpuTune(CurveOptimizer: -10), engine.GetCpuTuning(false).Kept);
        Assert.Null(engine.GetCpuTuning(false).RevertsAt);
    }

    [Fact]
    public void An_unkept_change_on_top_of_a_kept_one_goes_back_to_the_kept_one()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), CpuTuningOptions.Default);
        engine.ConfirmCpuTuning();

        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -15, PptWatts: 100), CpuTuningOptions.Default);
        _clock.Advance(CpuTuning.ConfirmWindow);

        Assert.Equal((-10, 162.0), (_cpu.CurveOptimizer, _cpu.Ppt));
        Assert.Equal(new CpuTune(CurveOptimizer: -10), engine.GetCpuTuning(false).Active);
    }

    [Fact]
    public void Several_unkept_changes_in_a_row_go_back_to_the_last_kept_settings()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -5), CpuTuningOptions.Default);
        _clock.Advance(TimeSpan.FromSeconds(40));
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -12), CpuTuningOptions.Default);
        _clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal(-12, _cpu.CurveOptimizer);

        _clock.Advance(TimeSpan.FromSeconds(20));

        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void Revert_puts_the_change_back_straight_away()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(PptWatts: 100), CpuTuningOptions.Default);

        var result = engine.RevertCpuTuning();

        Assert.True(result.Succeeded);
        Assert.True(_cpu.AtBios);
        Assert.False(engine.ConfirmCpuTuning().Succeeded);
    }

    [Fact]
    public void Keeping_after_the_window_is_too_late()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), CpuTuningOptions.Default);
        _clock.Advance(CpuTuning.ConfirmWindow);

        Assert.False(engine.ConfirmCpuTuning().Succeeded);
        Assert.Null(engine.GetCpuTuning(false).Kept);
    }

    [Fact]
    public void Reset_goes_back_to_the_BIOS_settings_now_and_forgets_the_kept_tune()
    {
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10, EdcAmps: 150), CpuTuningOptions.Default);
        engine.ConfirmCpuTuning();
        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -14), CpuTuningOptions.Default);

        var result = engine.ResetCpuTuning();

        Assert.True(result.Succeeded);
        Assert.True(_cpu.AtBios);
        Assert.Null(result.Cpu.Kept);
        Assert.Null(result.Cpu.Active);
        Assert.Null(result.Cpu.RevertsAt);
        Assert.Contains("restart", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unkept_Intel_change_restores_the_BIOS_power_limits()
    {
        _cpu = FakeCpu.Intel14900K(pl1: 253, pl2: 253);
        var engine = NewEngine();
        engine.SetCpuTuning(new CpuTune(Pl1Watts: 125, Pl2Watts: 200), CpuTuningOptions.Default);
        Assert.Equal((125, 200), (_cpu.Pl1, _cpu.Pl2));

        _clock.Advance(CpuTuning.ConfirmWindow);

        Assert.True(_cpu.AtBios);
    }

    // ---- Stress test before auto-apply ----

    [Fact]
    public void Auto_apply_is_refused_until_the_kept_tune_passes_the_stress_test()
    {
        var engine = NewEngine();
        Keep(engine, new CpuTune(CurveOptimizer: -10));

        var refused = engine.SetCpuAutoApply(true);

        Assert.False(refused.Succeeded);
        Assert.False(refused.Cpu.AutoApply);
    }

    [Fact]
    public void The_stress_test_needs_a_kept_tune_on_the_CPU()
    {
        var engine = NewEngine();
        Assert.False(engine.RunCpuStressTest(TestContext.Current.CancellationToken).Succeeded);

        engine.SetCpuTuning(new CpuTune(CurveOptimizer: -10), CpuTuningOptions.Default);
        Assert.False(engine.RunCpuStressTest(TestContext.Current.CancellationToken).Succeeded);

        Assert.Equal(0, _cpu.StressRuns);
    }

    [Fact]
    public void A_passed_stress_test_unlocks_auto_apply()
    {
        var engine = NewEngine();
        Keep(engine, new CpuTune(CurveOptimizer: -10));

        var test = engine.RunCpuStressTest(TestContext.Current.CancellationToken);
        var autoApply = engine.SetCpuAutoApply(true);

        Assert.True(test.Succeeded, test.Message);
        Assert.Equal(CpuTuning.StressTestLength, _cpu.LastStressDuration);
        Assert.True(test.Cpu.StressTestPassed);
        Assert.True(autoApply.Succeeded, autoApply.Message);
        Assert.True(autoApply.Cpu.AutoApply);
    }

    [Fact]
    public void A_failed_stress_test_puts_the_BIOS_settings_back_and_drops_the_tune()
    {
        var engine = NewEngine();
        Keep(engine, new CpuTune(CurveOptimizer: -15));
        _cpu.StressPasses = false;

        var test = engine.RunCpuStressTest(TestContext.Current.CancellationToken);

        Assert.False(test.Succeeded);
        Assert.True(_cpu.AtBios);
        Assert.Null(test.Cpu.Kept);
        Assert.False(engine.SetCpuAutoApply(true).Succeeded);
    }

    [Fact]
    public void A_WHEA_error_during_the_stress_test_fails_it()
    {
        var engine = NewEngine();
        Keep(engine, new CpuTune(CurveOptimizer: -15));
        _cpu.DuringStress = () => _cpu.WheaErrors.Add(_clock.GetUtcNow());

        var test = engine.RunCpuStressTest(TestContext.Current.CancellationToken);

        Assert.False(test.Succeeded);
        Assert.Contains("WHEA", test.Message);
        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void Keeping_a_different_tune_needs_a_new_stress_test()
    {
        var engine = NewEngine();
        Keep(engine, new CpuTune(CurveOptimizer: -10));
        engine.RunCpuStressTest(TestContext.Current.CancellationToken);
        engine.SetCpuAutoApply(true);

        var kept = Keep(engine, new CpuTune(CurveOptimizer: -15));

        Assert.False(kept.Cpu.StressTestPassed);
        Assert.False(kept.Cpu.AutoApply);
        Assert.False(engine.SetCpuAutoApply(true).Succeeded);
    }

    // ---- Re-apply after sign-in and resume ----

    [Fact]
    public void An_auto_apply_tune_comes_back_90_seconds_after_sign_in_never_at_boot()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -10, PptWatts: 120));
        Reboot();
        Assert.True(_cpu.AtBios);

        _service!.OnLogon();
        _clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(_cpu.AtBios);

        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal((-10, 120.0), (_cpu.CurveOptimizer, _cpu.Ppt));
        var cpu = NewEngine().GetCpuTuning(false);
        Assert.Equal(new CpuTune(CurveOptimizer: -10, PptWatts: 120), cpu.Active);
        Assert.Null(cpu.RevertsAt);
    }

    [Fact]
    public void An_auto_apply_tune_comes_back_after_resume_from_sleep()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -10));
        _cpu.PowerCycle();

        _service!.OnResume();
        Assert.Null(NewEngine().GetCpuTuning(false).Active);
        _clock.Advance(CpuTuning.ReapplyDelay);

        Assert.Equal(-10, _cpu.CurveOptimizer);
    }

    [Fact]
    public void A_kept_tune_without_auto_apply_is_not_re_applied()
    {
        Keep(NewEngine(), new CpuTune(CurveOptimizer: -10));
        Reboot();

        _service!.OnLogon();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(_cpu.AtBios);
    }

    // ---- Crash rule: skip re-apply, step back by 5, two strikes disable ----

    [Fact]
    public void After_an_unclean_shutdown_the_tune_is_not_re_applied_and_Curve_Optimizer_steps_back_by_5()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -15, PptWatts: 120));
        SignInAndWait();
        Assert.Equal(-15, _cpu.CurveOptimizer);

        Crash();
        SignInAndWait();

        Assert.True(_cpu.AtBios);
        var cpu = NewEngine().GetCpuTuning(false);
        Assert.Equal(new CpuTune(CurveOptimizer: -10, PptWatts: 120), cpu.Kept);
        Assert.Equal(1, cpu.Strikes);
        Assert.False(cpu.Disabled);
        Assert.Contains("-10", cpu.Notice);

        Reboot();
        SignInAndWait();
        Assert.Equal(-10, _cpu.CurveOptimizer);
    }

    [Fact]
    public void A_WHEA_error_while_tuned_is_a_strike_too()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -12));
        SignInAndWait();
        _clock.Advance(TimeSpan.FromHours(1));
        _cpu.WheaErrors.Add(_clock.GetUtcNow());

        Reboot();
        SignInAndWait();

        Assert.True(_cpu.AtBios);
        var cpu = NewEngine().GetCpuTuning(false);
        Assert.Equal(1, cpu.Strikes);
        Assert.Equal(-7, cpu.Kept!.CurveOptimizer);
        Assert.Contains("WHEA", cpu.Notice);
    }

    [Fact]
    public void A_WHEA_error_before_sleep_is_a_strike_and_the_tune_is_not_re_applied_on_resume()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -12));
        SignInAndWait();
        _clock.Advance(TimeSpan.FromHours(1));
        _cpu.WheaErrors.Add(_clock.GetUtcNow());
        _cpu.PowerCycle();

        _service!.OnResume();
        _clock.Advance(CpuTuning.ReapplyDelay);

        Assert.True(_cpu.AtBios);
        var cpu = NewEngine().GetCpuTuning(false);
        Assert.Equal(1, cpu.Strikes);
        Assert.Equal(-7, cpu.Kept!.CurveOptimizer);
        Assert.Contains("WHEA", cpu.Notice);

        // The same errors don't count again at the next restart.
        Reboot();
        SignInAndWait();
        Assert.Equal(1, NewEngine().GetCpuTuning(false).Strikes);
        Assert.Equal(-7, _cpu.CurveOptimizer);
    }

    [Fact]
    public void Two_strikes_turn_the_tune_off_until_a_new_stress_test()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -15));
        SignInAndWait();
        Crash();
        Reboot();
        SignInAndWait();
        Assert.Equal(-10, _cpu.CurveOptimizer);

        Crash();
        var cpu = NewEngine().GetCpuTuning(false);
        Assert.Equal(2, cpu.Strikes);
        Assert.True(cpu.Disabled);
        Assert.False(cpu.AutoApply);
        Assert.False(NewEngine().SetCpuAutoApply(true).Succeeded);

        Reboot();
        SignInAndWait();
        Assert.True(_cpu.AtBios);
    }

    [Fact]
    public void Stepping_back_never_makes_Curve_Optimizer_positive()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -3, EdcAmps: 150));
        SignInAndWait();

        Crash();

        Assert.Equal(new CpuTune(EdcAmps: 150), NewEngine().GetCpuTuning(false).Kept);
    }

    [Fact]
    public void A_crash_while_the_CPU_ran_its_BIOS_settings_is_not_a_strike()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -10));
        SignInAndWait();
        NewEngine().ResetCpuTuning();

        Crash();

        Assert.Equal(0, NewEngine().GetCpuTuning(false).Strikes);
    }

    [Fact]
    public void A_crash_logged_by_Windows_after_the_service_stopped_still_counts()
    {
        StressTestedAutoApply(new CpuTune(CurveOptimizer: -10));
        SignInAndWait();
        _service!.Shutdown();
        new TuningSafety(_tuningStore).EndSession();
        _cpu.Crashes.Add(_clock.GetUtcNow());
        _cpu.PowerCycle();

        StartService();
        SignInAndWait();

        Assert.True(_cpu.AtBios);
        Assert.Equal(1, NewEngine().GetCpuTuning(false).Strikes);
    }

    // ---- Helpers ----

    private CpuTuningResult Keep(Engine engine, CpuTune tune)
    {
        Assert.True(engine.SetCpuTuning(tune, new CpuTuningOptions(Advanced: true)).Succeeded);
        var kept = engine.ConfirmCpuTuning();
        Assert.True(kept.Succeeded, kept.Message);
        return kept;
    }

    private void StressTestedAutoApply(CpuTune tune)
    {
        var engine = NewEngine();
        Keep(engine, tune);
        Assert.True(engine.RunCpuStressTest(TestContext.Current.CancellationToken).Succeeded);
        Assert.True(engine.SetCpuAutoApply(true).Succeeded);
    }

    /// <summary>A clean restart: the service stops cleanly, the CPU loses its runtime settings, the service starts again.</summary>
    private void Reboot()
    {
        _service!.Shutdown();
        new TuningSafety(_tuningStore).EndSession();
        _cpu.PowerCycle();
        StartService();
    }

    private void SignInAndWait()
    {
        _service!.OnLogon();
        _clock.Advance(CpuTuning.ReapplyDelay);
    }

    /// <summary>A crash, freeze or power cut: nothing stops cleanly.</summary>
    private void Crash()
    {
        _cpu.Crashes.Add(_clock.GetUtcNow());
        _cpu.PowerCycle();
        _clock.Advance(TimeSpan.FromMinutes(1));
        StartService();
    }

    private static TuningControl Control(CpuTuningReport report, string id) => report.Controls.Single(c => c.Id == id);

    private CpuTuning StartService(bool begin = true)
    {
        _service?.Dispose();
        var safety = new TuningSafety(_tuningStore);
        _service = new CpuTuning(_cpu, safety, _profiles, _cpu, _cpu, _clock);
        _serviceCpu = _cpu;
        if (begin)
            _service.RecoverAtStartup(safety.BeginSession());
        return _service;
    }

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo()),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        clock: _clock,
        cpuTuning: _service is not null && _serviceCpu == _cpu ? _service : StartService());
}
