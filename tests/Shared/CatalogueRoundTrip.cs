using Elynt.Core;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Tests.Shared;

/// <summary>
/// The core round trip every catalogue tweak must pass, driven only through Engine commands:
/// snapshot → apply → Detect says on → revert → state equals the snapshot exactly.
/// Shared by the fake-adapter logic tests and the real-Windows CI tests.
/// </summary>
public static class CatalogueRoundTrip
{
    public static void Run(Engine engine, IRegistry registry, TweakDefinition tweak)
    {
        var tweakId = tweak.Id;
        var before = RegistrySnapshot.Take(registry, tweak);

        // Confirmed, so destructive tweaks whose steps the journal can still put back are covered too.
        var applied = engine.Apply(tweakId, new ApplyOptions(ConfirmDestructive: true));
        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == tweakId).State);

        var reverted = engine.Revert(tweakId);
        Assert.True(reverted.Succeeded, reverted.Message);
        Assert.Equal(before, RegistrySnapshot.Take(registry, tweak));
    }
}

/// <summary>Every registry location a tweak touches, rendered with existence, type and raw bytes.</summary>
public static class RegistrySnapshot
{
    public static IReadOnlyList<string> Take(IRegistry registry, TweakDefinition tweak) =>
        tweak.Steps.OfType<RegistryValueStep>()
            .Select(step => step.Location)
            .Select(location =>
            {
                var parts = location.KeyPath.Split('\\');
                var key = string.Join(", ", Enumerable.Range(1, parts.Length)
                    .Select(n => string.Join('\\', parts[..n]))
                    .Select(path => $"{path} {(registry.KeyExists(location.Root, path) ? "present" : "absent")}"));
                var value = registry.GetValue(location) is { } v
                    ? $"{v.Kind} {Convert.ToHexString(v.Data)}"
                    : "value absent";
                return $"{location}: {key}, {value}";
            })
            .ToList();
}
