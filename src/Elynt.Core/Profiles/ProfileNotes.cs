namespace Elynt.Core.Profiles;

public enum ProfileNoteKind
{
    Applied,
    Reverted,
    /// <summary>Another game's profile was already in charge.</summary>
    Skipped,
    /// <summary>Some change couldn't be made or undone.</summary>
    Problem,
}

/// <summary>A quiet tray note about a per-game profile, for one user.</summary>
public sealed record ProfileNote(string User, ProfileNoteKind Kind, string Title, string Message);

/// <summary>
/// Notes waiting for each user's app window. The service posts them as profiles run; the app takes them for its tray,
/// where the user can turn them off. Only the newest few are kept, so an app that never asks can't fill memory.
/// </summary>
public sealed class ProfileNotes
{
    private const int MaxPerUser = 20;
    private readonly Dictionary<string, Queue<ProfileNote>> _byUser = [];
    private readonly Lock _gate = new();

    public void Post(ProfileNote note)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(note.User, out var queue))
                _byUser[note.User] = queue = new();
            queue.Enqueue(note);
            while (queue.Count > MaxPerUser)
                queue.Dequeue();
        }
    }

    /// <summary>The user's waiting notes, oldest first. Each note is returned once.</summary>
    public IReadOnlyList<ProfileNote> Take(string user)
    {
        lock (_gate)
        {
            if (!_byUser.Remove(user, out var queue))
                return [];
            return [.. queue];
        }
    }
}
