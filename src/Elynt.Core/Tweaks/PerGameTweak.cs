using System.Text.RegularExpressions;

namespace Elynt.Core.Tweaks;

/// <summary>
/// A tweak that is applied to one game at a time, such as turning off fullscreen optimizations for one .exe.
/// The catalogue lists <see cref="Template"/> so the user can pick a game; each game then gets its own tweak, with
/// the id <c>template-id|C:\path\to\game.exe</c>, that is applied, detected and undone like any other.
/// </summary>
/// <param name="Template">The description shown before a game is chosen. It has no steps.</param>
/// <param name="StepsFor">The steps for one game, given its full .exe path.</param>
public sealed partial class PerGameTweak(TweakDefinition template, Func<string, IReadOnlyList<TweakStep>> stepsFor)
{
    public const char Separator = '|';

    public TweakDefinition Template { get; } = template with { PerGame = true, Steps = [] };

    public string Id => Template.Id;

    public string IdFor(string exePath) => $"{Id}{Separator}{exePath}";

    /// <summary>The tweak for one game, or null when <paramref name="exePath"/> isn't a full path to a program.</summary>
    public TweakDefinition? For(string exePath) => IsProgramPath(exePath)
        ? Template with
        {
            Id = IdFor(exePath),
            Name = $"{Template.Name}: {exePath[(exePath.LastIndexOf('\\') + 1)..]}",
            Steps = stepsFor(exePath),
            PerGame = false,
        }
        : null;

    /// <summary>A full Windows path to an .exe, with no relative parts and no characters Windows doesn't allow.</summary>
    public static bool IsProgramPath(string path) =>
        ProgramPath().IsMatch(path) && !path.Split('\\').Any(part => part is "." or "..");

    [GeneratedRegex(@"^[A-Za-z]:\\([^<>:""/\\|?*\x00-\x1F]+\\)*[^<>:""/\\|?*\x00-\x1F]+\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramPath();
}
