using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Tuning;

/// <summary>
/// GPU tuning for every vendor adapter. One instance lives for the whole service (GPU settings are machine-wide),
/// and every Engine routes its GPU commands here. Every change reverts after <see cref="ConfirmWindow"/> unless
/// confirmed; only confirmed settings are re-applied, and only after a clean shutdown.
/// </summary>
public sealed partial class GpuTuning : IDisposable
{
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<IGpuTuningAdapter> _adapters;
    private readonly TuningSafety _safety;
    private readonly TimeProvider _clock;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly ConfirmOrRevert _probation;
    private readonly Dictionary<string, string> _notices = [];
    private string? _pageNotice;

    public GpuTuning(IReadOnlyList<IGpuTuningAdapter> adapters, TuningSafety safety, TimeProvider? clock = null, ILogger<GpuTuning>? logger = null)
    {
        _adapters = adapters;
        _safety = safety;
        _clock = clock ?? TimeProvider.System;
        _log = logger ?? NullLogger<GpuTuning>.Instance;
        _probation = new ConfirmOrRevert(_clock, _gate, (key, ex) => _log.LogError(ex, "Putting back {Gpu} failed", key));
    }

    public GpuTuningReport Describe()
    {
        lock (_gate)
        {
            var views = new List<GpuTuningView>();
            var problems = new List<string>();
            foreach (var adapter in _adapters)
            {
                try
                {
                    if (adapter.UnavailableReason is { } why)
                    {
                        problems.Add(why);
                        continue;
                    }
                    views.AddRange(adapter.Devices().Select(device => View(adapter, device)));
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Couldn't read GPU tuning");
                    problems.Add($"Couldn't read the GPU's tuning settings: {ex.Message}");
                }
            }

            var notice = _pageNotice ?? (views.Count == 0
                ? (problems.Count > 0 ? string.Join(" ", problems) : "No GPU on this PC can be tuned from Windows.")
                : null);
            return new GpuTuningReport(views, notice);
        }
    }

    /// <summary>Sets manual values. Each must be inside the range the vendor SDK reports.</summary>
    public TuningResult Set(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            if (IsLocked(device))
                return Locked(adapter, device);
            if (HeldByProfile(device))
                return HeldByProfile(adapter, device);
            if (values.Count == 0)
                return Result(false, "Nothing to change.", adapter, device);

            if (WhyNot(device, values) is { } why)
                return Result(false, why + " Nothing was changed.", adapter, device);

            return Change(adapter, device, () => adapter.Write(device.Id, values), mode: null);
        }
    }

    /// <summary>Runs the vendor's auto overclock or undervolt, on the same 15 s probation as a manual change.</summary>
    public TuningResult AutoTune(string deviceId, AutoTuneKind kind)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            if (IsLocked(device))
                return Locked(adapter, device);
            if (HeldByProfile(device))
                return HeldByProfile(adapter, device);
            if (!device.AutoTunes.Contains(kind))
                return Result(false, "This GPU's driver doesn't offer that automatic tuning. Nothing was changed.", adapter, device);

            return Change(adapter, device, () => adapter.AutoTune(device.Id, kind), mode: kind.ToString());
        }
    }

    /// <summary>Keeps the change that is waiting, and re-applies it after clean restarts.</summary>
    public TuningResult Confirm(string deviceId)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            if (!_probation.Confirm(Key(device)))
                return Result(false, "Nothing is waiting to be kept. A change that wasn't kept in time has already gone back.", adapter, device);

            try
            {
                if (adapter.IsAtDefaults(device.Id))
                    _safety.MarkDefaults(Key(device));
                else
                    _safety.SaveConfirmed(Key(device), new SavedTuning(_pendingModes.GetValueOrDefault(Key(device)), adapter.Read(device.Id)));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't save the confirmed settings for {Gpu}", device.Id);
                return Result(false, $"Kept for now, but couldn't save the settings for next time: {ex.Message}", adapter, device);
            }
            finally
            {
                _pendingModes.Remove(Key(device));
            }

            _notices.Remove(Key(device));
            _log.LogInformation("Confirmed tuning on {Gpu}", device.Id);
            return Result(true, "Kept. These settings come back after each clean restart.", adapter, device);
        }
    }

    /// <summary>Puts the waiting change back now instead of waiting for the countdown.</summary>
    public TuningResult Revert(string deviceId)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            if (!_probation.RevertNow(Key(device)))
                return Result(false, "Nothing is waiting to be undone.", adapter, device);
            _notices.Remove(Key(device));
            return Result(true, "Put back to the last kept settings.", adapter, device);
        }
    }

    /// <summary>
    /// The user accepted the vendor's warning: tells the driver (Intel's overclock waiver) and remembers it, so tuning
    /// stays unlocked after restarts.
    /// </summary>
    public TuningResult AcceptWarning(string deviceId)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            if (device.Warning is null)
                return Result(true, "This GPU has no warning to accept.", adapter, device);

            try
            {
                adapter.AcceptWarning(device.Id);
                _safety.AcceptWarning(Key(device));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Accepting the warning on {Gpu} failed", device.Id);
                return Result(false, $"The driver didn't unlock tuning: {ex.Message}", adapter, device);
            }
            _waiverSet.Add(Key(device));
            _log.LogInformation("The user accepted the tuning warning for {Gpu}", device.Id);
            return Result(true, "Warning accepted. Tuning is unlocked for this GPU.", adapter, device);
        }
    }

    /// <summary>Factory settings, straight away and without probation. Also forgets the confirmed settings.</summary>
    public TuningResult ResetToDefaults(string deviceId)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            var key = Key(device);
            _probation.Cancel(key);
            _pendingModes.Remove(key);
            // Factory settings win over a running game's profile too; nothing is put back after the game.
            _profileHolds.Remove(key);
            try
            {
                adapter.ResetToDefaults(device.Id);
                _safety.MarkDefaults(key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Resetting {Gpu} failed", device.Id);
                return Result(false, $"Couldn't reset the GPU: {ex.Message}", adapter, device);
            }
            _notices.Remove(key);
            _log.LogInformation("Reset {Gpu} to defaults", device.Id);
            return Result(true, "Back on factory settings.", adapter, device);
        }
    }

    /// <summary>
    /// At service start: after an unclean end, puts every GPU that was tuned back on defaults and drops its saved
    /// settings so a bad tune can never loop; after a clean end, re-applies the confirmed settings.
    /// </summary>
    public void RecoverAtStartup(SessionStart start)
    {
        lock (_gate)
        {
            var devices = _adapters
                .Where(a => SafeUnavailable(a) is null)
                .SelectMany(a => SafeDevices(a).Select(d => (Adapter: a, Device: d)))
                .ToList();

            // A driver such as IGCL forgets the accepted warning when the process ends, and may refuse even a reset
            // without it, so tell it again before touching anything.
            foreach (var (adapter, device) in devices)
                TryRestoreWaiver(adapter, device);

            if (start.Unclean)
            {
                var reset = new List<string>();
                foreach (var (adapter, device) in devices.Where(d => start.WasLive(Key(d.Device))))
                {
                    try
                    {
                        adapter.ResetToDefaults(device.Id);
                        _safety.MarkDefaults(Key(device));
                        reset.Add(device.Name);
                        _log.LogWarning("Windows didn't shut down cleanly; {Gpu} is back on defaults", device.Id);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Couldn't reset {Gpu} after an unclean shutdown", device.Id);
                    }
                }
                if (reset.Count > 0)
                    _pageNotice = $"Windows didn't shut down cleanly last time, so {string.Join(" and ", reset)} " +
                                  "went back to default settings and your saved tuning wasn't re-applied. Set it again if you trust it.";
                return;
            }

            foreach (var (adapter, device) in devices)
            {
                if (start.Saved.GetValueOrDefault(Key(device)) is not { } saved)
                    continue;
                try
                {
                    _safety.MarkLive(Key(device));
                    if (saved.Mode is { } mode && Enum.TryParse<AutoTuneKind>(mode, out var kind))
                        adapter.AutoTune(device.Id, kind);
                    else
                        adapter.Write(device.Id, saved.Values);
                    _log.LogInformation("Re-applied the confirmed tuning on {Gpu}", device.Id);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Re-applying the tuning on {Gpu} failed; resetting it", device.Id);
                    TryReset(adapter, device);
                }
            }
        }
    }

    /// <summary>
    /// A clean stop: unconfirmed changes and a running game's profile go back, then the session is marked as ended cleanly.
    /// </summary>
    public void Shutdown()
    {
        lock (_gate)
        {
            _probation.RevertAll();
            EndProfile();
            _safety.EndSession();
        }
    }

    public void Dispose() => _probation.Dispose();

    private readonly Dictionary<string, string?> _pendingModes = [];

    /// <summary>Devices whose driver has been told, in this run, that the user accepted the warning.</summary>
    private readonly HashSet<string> _waiverSet = [];

    private TuningResult Change(IGpuTuningAdapter adapter, GpuDevice device, Action change, string? mode)
    {
        var key = Key(device);
        TryRestoreWaiver(adapter, device);
        var waiting = _probation.Deadline(key) is not null;
        Action revert;
        try
        {
            revert = waiting ? () => { } : WayBack(adapter, device);
            _safety.MarkLive(key);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Couldn't record {Gpu}'s settings before changing them", device.Id);
            return Result(false, $"Couldn't save the current settings before changing them: {ex.Message} Nothing was changed.", adapter, device);
        }

        try
        {
            change();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Changing {Gpu} failed; putting it back", device.Id);
            if (waiting)
                _probation.RevertNow(key);
            else
                Run(revert, adapter, device);
            _pendingModes.Remove(key);
            return Result(false, $"The driver refused the change: {ex.Message} The previous settings are back.", adapter, device);
        }

        _pendingModes[key] = mode;
        _notices.Remove(key);
        _probation.Start(key, ConfirmWindow, revert);
        _log.LogInformation("Changed {Gpu}; reverting in {Seconds} s unless confirmed", device.Id, ConfirmWindow.TotalSeconds);
        return Result(true, $"Changed. Keep these settings within {ConfirmWindow.TotalSeconds:0} seconds or they go back.", adapter, device);
    }

    /// <summary>How to get back to what the card runs now: factory reset, the confirmed tune, or the current values.</summary>
    private Action WayBack(IGpuTuningAdapter adapter, GpuDevice device)
    {
        var key = Key(device);
        if (adapter.IsAtDefaults(device.Id))
        {
            return () =>
            {
                _pendingModes.Remove(key);
                adapter.ResetToDefaults(device.Id);
                _safety.MarkDefaults(key);
                _notices[key] = "The change wasn't kept, so the GPU is back on default settings.";
                _log.LogInformation("Reverted {Gpu} to defaults", device.Id);
            };
        }

        var confirmed = _safety.Confirmed(key);
        var values = adapter.Read(device.Id);
        return () =>
        {
            _pendingModes.Remove(key);
            if (confirmed?.Mode is { } mode && Enum.TryParse<AutoTuneKind>(mode, out var kind))
            {
                adapter.ResetToDefaults(device.Id);
                adapter.AutoTune(device.Id, kind);
            }
            else
            {
                adapter.Write(device.Id, confirmed?.Values ?? values);
            }
            _notices[key] = "The change wasn't kept, so the GPU is back on its previous settings.";
            _log.LogInformation("Reverted {Gpu} to its previous settings", device.Id);
        };
    }

    private void Run(Action revert, IGpuTuningAdapter adapter, GpuDevice device)
    {
        try
        {
            revert();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Putting {Gpu} back failed; resetting it", device.Id);
            TryReset(adapter, device);
        }
    }

    private void TryReset(IGpuTuningAdapter adapter, GpuDevice device)
    {
        try
        {
            adapter.ResetToDefaults(device.Id);
            _safety.MarkDefaults(Key(device));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Resetting {Gpu} failed too", device.Id);
        }
    }

    private (IGpuTuningAdapter, GpuDevice)? Find(string deviceId)
    {
        foreach (var adapter in _adapters)
        {
            if (SafeUnavailable(adapter) is not null)
                continue;
            if (SafeDevices(adapter).FirstOrDefault(d => d.Id == deviceId) is { } device)
                return (adapter, device);
        }
        return null;
    }

    private string? SafeUnavailable(IGpuTuningAdapter adapter)
    {
        try
        {
            return adapter.UnavailableReason;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private IReadOnlyList<GpuDevice> SafeDevices(IGpuTuningAdapter adapter)
    {
        try
        {
            return adapter.Devices();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't list tunable GPUs");
            return [];
        }
    }

    private GpuTuningView View(IGpuTuningAdapter adapter, GpuDevice device)
    {
        var values = adapter.Read(device.Id);
        return new GpuTuningView(
            device.Id,
            device.Name,
            device.Vendor,
            device.Controls.Select(c => new TuningControl(c.Id, c.Name, c.Unit, c.Min, c.Max, c.Step, c.Default, values.GetValueOrDefault(c.Id, c.Default))).ToList(),
            device.AutoTunes,
            adapter.IsAtDefaults(device.Id),
            _probation.Deadline(Key(device)),
            _notices.GetValueOrDefault(Key(device)),
            device.Warning,
            IsLocked(device),
            Readouts(adapter, device));
    }

    private IReadOnlyList<GpuReadout>? Readouts(IGpuTuningAdapter adapter, GpuDevice device)
    {
        if (adapter is not IGpuReadouts sensors)
            return null;
        try
        {
            return sensors.Readouts(device.Id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read {Gpu}'s sensors", device.Id);
            return [];
        }
    }

    private void TryRestoreWaiver(IGpuTuningAdapter adapter, GpuDevice device)
    {
        var key = Key(device);
        if (device.Warning is null || _waiverSet.Contains(key) || !_safety.WarningAccepted(key))
            return;
        try
        {
            adapter.AcceptWarning(device.Id);
            _waiverSet.Add(key);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Couldn't tell the driver again that the warning for {Gpu} was accepted", device.Id);
        }
    }

    /// <summary>Why these values can't go on the card, or null: every control must exist and be inside the SDK's range.</summary>
    private static string? WhyNot(GpuDevice device, IReadOnlyDictionary<string, int> values)
    {
        foreach (var (id, value) in values)
        {
            var spec = device.Controls.FirstOrDefault(c => c.Id == id);
            if (spec is null)
                return $"This GPU has no '{id}' setting.";
            if (spec.WhyNot(value) is { } why)
                return why;
        }
        return null;
    }

    private bool IsLocked(GpuDevice device) => device.Warning is not null && !_safety.WarningAccepted(Key(device));

    private TuningResult Locked(IGpuTuningAdapter adapter, GpuDevice device) =>
        Result(false, "Read and accept the warning first; tuning stays locked until you do. Nothing was changed.", adapter, device);

    private TuningResult Result(bool succeeded, string message, IGpuTuningAdapter adapter, GpuDevice device)
    {
        GpuTuningView? view = null;
        try
        {
            view = View(adapter, device);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read {Gpu}", device.Id);
        }
        return new TuningResult(succeeded, message, view);
    }

    private static TuningResult NoSuchGpu(string deviceId) => new(false, $"There is no tunable GPU called '{deviceId}'.", null);

    private static string Key(GpuDevice device) => "gpu:" + device.Id;
}
