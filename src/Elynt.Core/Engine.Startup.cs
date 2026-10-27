using Elynt.Core.Startup;

namespace Elynt.Core;

/// <summary>The Startup page's commands. The logic lives in <see cref="StartupManager"/>.</summary>
public sealed partial class Engine
{
    public IReadOnlyList<StartupItemView> ListStartupItems() => Startup.List();

    public StartupCommandResult SetStartupItemEnabled(string itemId, bool enabled) => Startup.SetEnabled(itemId, enabled);

    public StartupCommandResult DeleteStartupItem(string itemId, bool confirmDestructive) => Startup.Delete(itemId, confirmDestructive);

    public IReadOnlyList<StartupItemView> CheckNewStartupItems() => Startup.CheckNew();
}
