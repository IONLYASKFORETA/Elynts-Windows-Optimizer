using Elynt.Core.Tweaks;

namespace Elynt.Core.Games;

/// <summary>Plain-words warnings that name the installed games a tweak affects. They warn; nothing is blocked.</summary>
public static class AntiCheatWarning
{
    /// <summary>One sentence per severity, e.g. "Turning this on may stop VALORANT (Riot Vanguard) from launching."</summary>
    public static string? For(IReadOnlyList<GameConflict> affected)
    {
        if (affected.Count == 0)
            return null;

        var sentences = affected
            .GroupBy(c => c.Severity)
            .OrderByDescending(g => g.Key)
            .Select(bySeverity =>
            {
                var games = JoinAnd(bySeverity
                    .GroupBy(c => c.AntiCheat)
                    .Select(byAntiCheat => $"{string.Join(", ", byAntiCheat.Select(c => c.Game).Distinct())} ({Name(byAntiCheat.Key)})")
                    .ToList());
                return bySeverity.Key switch
                {
                    AntiCheatSeverity.WontLaunch => $"Turning this on may stop {games} from launching.",
                    AntiCheatSeverity.Restricted => $"With this on, {games} may only run with limited modes or matchmaking.",
                    AntiCheatSeverity.Kick => $"With this on, {games} may kick you from matches.",
                    _ => $"With this on, the anti-cheat in {games} may block the tool this uses.",
                };
            });
        return string.Join(" ", sentences);
    }

    public static string Name(AntiCheat antiCheat) => antiCheat switch
    {
        AntiCheat.Vanguard => "Riot Vanguard",
        AntiCheat.FaceIt => "FACEIT",
        AntiCheat.EasyAntiCheat => "Easy Anti-Cheat",
        AntiCheat.BattlEye => "BattlEye",
        AntiCheat.Ricochet => "Ricochet",
        AntiCheat.Javelin => "EA Javelin",
        _ => antiCheat.ToString(),
    };

    private static string JoinAnd(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";
}
