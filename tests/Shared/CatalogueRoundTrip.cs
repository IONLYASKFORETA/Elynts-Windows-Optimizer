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
    /// <summary>A program path for per-game tweaks. Nothing needs to exist there.</summary>
    public const string ExampleGame = @"C:\ElyntOptimizerTests\example-game.exe";

    /// <summary>
    /// Every tweak that can be applied: each catalogue tweak, with a per-game tweak's template replaced by its
    /// instance for <see cref="ExampleGame"/>.
    /// </summary>
    public static IEnumerable<string> ApplicableIds(Catalogue catalogue) =>
        catalogue.Tweaks.Select(t => t.PerGame ? $"{t.Id}{PerGameTweak.Separator}{ExampleGame}" : t.Id);

    public static void Run(Engine engine, SystemAdapters system, TweakDefinition tweak)
    {
        var tweakId = tweak.Id;
        var before = SystemSnapshot.Take(system, tweak);

        // Confirmed, so destructive tweaks whose steps the journal can still put back are covered too.
        var applied = engine.Apply(tweakId, new ApplyOptions(ConfirmDestructive: true));
        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == tweakId).State);

        var reverted = engine.Revert(tweakId);
        Assert.True(reverted.Succeeded, reverted.Message);
        Assert.Equal(before, SystemSnapshot.Take(system, tweak));
    }
}

/// <summary>Everything a tweak touches: registry values, boot settings and power plans.</summary>
public static class SystemSnapshot
{
    public static IReadOnlyList<string> Take(SystemAdapters system, TweakDefinition tweak)
    {
        var lines = RegistrySnapshot.Take(system.Registry, tweak).ToList();
        foreach (var target in tweak.Steps.Select(step => step.Target))
        {
            switch (target)
            {
                case BootSettingTarget boot:
                    lines.Add($"boot {boot.Element}: {system.Boot!.Get(boot.Element) ?? "not set"}");
                    break;
                case PowerPlanTarget plan:
                    lines.Add($"active power plan: {system.Power!.GetActive()}");
                    lines.Add($"power plan {plan.Plan}: {(system.Power.Exists(plan.Plan) ? "present" : "absent")}");
                    break;
                case ScanExclusionTarget exclusion:
                    lines.Add($"scan exclusion {exclusion.Folder}: {(system.ScanExclusions!.Contains(exclusion.Folder) ? "present" : "absent")}");
                    break;
            }
        }
        return lines;
    }
}

/// <summary>Every registry location a tweak touches, rendered with existence, type and raw bytes.</summary>
public static class RegistrySnapshot
{
    public static IReadOnlyList<string> Take(IRegistry registry, TweakDefinition tweak) =>
        tweak.Steps.Select(step => step.Target).OfType<RegistryTarget>()
            .Select(target => target.Location)
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
