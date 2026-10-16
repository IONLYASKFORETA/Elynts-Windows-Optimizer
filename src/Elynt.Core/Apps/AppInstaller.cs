namespace Elynt.Core.Apps;

/// <summary>A catalogue app as the UI shows it.</summary>
public sealed record AppView(string Id, string Name, string Group, string Description, bool InAllRuntimes);

/// <summary>Runs the Apps section's commands: lists the catalogue and works through the install queue.</summary>
internal sealed class AppInstaller(AppCatalogue catalogue)
{
    public IReadOnlyList<AppView> ListApps() =>
        catalogue.Apps.Select(a => new AppView(a.Id, a.Name, a.Group, a.Description, a.InAllRuntimes)).ToList();
}
