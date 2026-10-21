using Microsoft.Extensions.Logging;

namespace Elynt.Core.Tuning;

/// <summary>
/// GPU tuning held by a running per-game profile. It skips the confirm window (nobody is watching while a game
/// starts) but keeps the rest of the safety: the GPU is marked live before it changes, so a crash mid-game puts it
/// back on defaults, and a clean stop puts back what ran before the game.
/// </summary>
public sealed partial class GpuTuning
{
    /// <summary>The way back to what each GPU ran before the profile, by safety key.</summary>
    private readonly Dictionary<string, (string DeviceId, Action PutBack)> _profileHolds = [];

    /// <summary>Why these settings can't be part of a profile, or null when they can. Checked on save and again at game start.</summary>
    public string? WhyNotInProfile(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not var (_, device))
                return $"There is no tunable GPU called '{deviceId}'.";
            return WhyNotInProfile(device, values);
        }
    }

    private string? WhyNotInProfile(GpuDevice device, IReadOnlyDictionary<string, int> values)
    {
        if (IsLocked(device))
            return $"Accept the {device.Name} warning on the Overclocking page first.";
        if (values.Count == 0)
            return $"The GPU tuning for {device.Name} has no settings.";
        return WhyNot(device, values) is { } why ? $"{device.Name}: {why}" : null;
    }

    /// <summary>Puts a profile's settings on the GPU until <see cref="EndProfile"/>.</summary>
    public TuningResult ApplyForProfile(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            if (Find(deviceId) is not { } found)
                return NoSuchGpu(deviceId);
            var (adapter, device) = found;
            var key = Key(device);
            if (WhyNotInProfile(device, values) is { } why)
                return Result(false, why + " Nothing was changed.", adapter, device);
            if (!_safety.ProfilesAllowed(key))
                return Result(false, $"Windows didn't shut down cleanly while {device.Name} was tuned, so game profiles leave it " +
                                     "alone until you keep a tune on the Overclocking page again. Nothing was changed.", adapter, device);

            // A change still waiting to be kept goes back first, so the way back after the game is the kept state.
            _probation.RevertNow(key);
            TryRestoreWaiver(adapter, device);

            Action putBack;
            try
            {
                putBack = _profileHolds.Remove(key, out var held) ? held.PutBack : ProfileWayBack(adapter, device);
                _safety.MarkLive(key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't record {Gpu}'s settings before the profile changed them", device.Id);
                return Result(false, $"Couldn't save the current settings before changing them: {ex.Message} Nothing was changed.", adapter, device);
            }

            try
            {
                adapter.Write(device.Id, values);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "The profile's change to {Gpu} failed; putting it back", device.Id);
                Run(putBack, adapter, device);
                return Result(false, $"The driver refused the change: {ex.Message} The previous settings are back.", adapter, device);
            }

            _profileHolds[key] = (device.Id, putBack);
            _notices.Remove(key);
            _log.LogInformation("A game profile tuned {Gpu}", device.Id);
            return Result(true, "Tuned for the game.", adapter, device);
        }
    }

    /// <summary>The game closed: every GPU a profile tuned goes back to what it ran before. Returns what couldn't.</summary>
    public IReadOnlyList<string> EndProfile()
    {
        lock (_gate)
        {
            var problems = new List<string>();
            foreach (var (key, (deviceId, putBack)) in _profileHolds.ToList())
            {
                _profileHolds.Remove(key);
                try
                {
                    putBack();
                    _log.LogInformation("Put {Gpu} back after the game", deviceId);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Putting {Gpu} back after the game failed; resetting it", deviceId);
                    problems.Add($"Couldn't put the GPU back: {ex.Message}");
                    if (Find(deviceId) is var (adapter, device))
                        TryReset(adapter, device);
                }
            }
            return problems;
        }
    }

    /// <summary>How to get back to what the card runs now, without the "wasn't kept" notice a probation revert leaves.</summary>
    private Action ProfileWayBack(IGpuTuningAdapter adapter, GpuDevice device)
    {
        var key = Key(device);
        if (adapter.IsAtDefaults(device.Id))
        {
            return () =>
            {
                adapter.ResetToDefaults(device.Id);
                _safety.MarkDefaults(key);
            };
        }

        var confirmed = _safety.Confirmed(key);
        var values = adapter.Read(device.Id);
        return () =>
        {
            if (confirmed?.Mode is { } mode && Enum.TryParse<AutoTuneKind>(mode, out var kind))
            {
                adapter.ResetToDefaults(device.Id);
                adapter.AutoTune(device.Id, kind);
            }
            else
            {
                adapter.Write(device.Id, confirmed?.Values ?? values);
            }
        };
    }

    private bool HeldByProfile(GpuDevice device) => _profileHolds.ContainsKey(Key(device));

    private TuningResult HeldByProfile(IGpuTuningAdapter adapter, GpuDevice device) =>
        Result(false, "A game's profile is tuning this GPU while the game runs. Change it after the game closes. Nothing was changed.", adapter, device);
}
