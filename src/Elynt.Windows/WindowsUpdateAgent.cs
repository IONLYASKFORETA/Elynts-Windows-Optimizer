using Elynt.Core.UpdateControl;

namespace Elynt.Windows;

/// <summary>
/// The Windows Update Agent API (Microsoft.Update.Session, wuapi.dll) through late-bound COM. It searches, downloads
/// and installs on request, the way Settings' "Check for updates" does. Every call of one run must come from the
/// same thread, which <see cref="ManualUpdate"/> does by running the whole check on one background thread.
/// </summary>
public sealed class WindowsUpdateAgent : IWindowsUpdateAgent
{
    private const string ClientName = "Elynt Optimizer";

    /// <summary>The "Upgrades" classification: Windows 11 feature updates.</summary>
    private const string UpgradesCategory = "3689bdc8-b205-4af4-8d4a-a63924c5e9d5";

    // IUpdate.Type: 1 software, 2 driver. OperationResultCode: 2 succeeded, 3 succeeded with errors.
    private const int DriverType = 2;
    private const int Succeeded = 2;
    private const int SucceededWithErrors = 3;

    private dynamic? _session;
    private readonly Dictionary<string, dynamic> _updates = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AvailableUpdate> Search()
    {
        _updates.Clear();
        _session = Create("Microsoft.Update.Session");
        _session.ClientApplicationID = ClientName;
        var searcher = _session.CreateUpdateSearcher();
        var result = searcher.Search("IsInstalled=0 and IsHidden=0");
        if ((int)result.ResultCode is not (Succeeded or SucceededWithErrors))
            throw new InvalidOperationException($"Windows Update couldn't search (result code {(int)result.ResultCode}).");

        var found = new List<AvailableUpdate>();
        var updates = result.Updates;
        for (var i = 0; i < (int)updates.Count; i++)
        {
            var update = updates.Item(i);
            // Optional updates (previews, "browse only") are what Settings lists under Optional updates; skip them.
            if ((bool)update.BrowseOnly)
                continue;
            string id = update.Identity.UpdateID;
            _updates[id] = update;
            found.Add(new AvailableUpdate(id, (string)update.Title, KindOf(update)));
        }
        return found;
    }

    public void Download(AvailableUpdate update)
    {
        var item = Find(update);
        if ((bool)item.IsDownloaded)
            return;
        var downloader = _session!.CreateUpdateDownloader();
        downloader.ClientApplicationID = ClientName;
        downloader.Updates = Collection(item);
        var result = downloader.Download();
        if ((int)result.ResultCode is not (Succeeded or SucceededWithErrors))
            throw new InvalidOperationException($"Download failed (0x{(int)result.GetUpdateResult(0).HResult:X8}).");
    }

    public InstallOutcome Install(AvailableUpdate update)
    {
        var item = Find(update);
        if (!(bool)item.EulaAccepted)
            item.AcceptEula();
        var installer = _session!.CreateUpdateInstaller();
        installer.ClientApplicationID = ClientName;
        installer.Updates = Collection(item);
        var result = installer.Install();
        var code = (int)result.ResultCode;
        var ok = code is Succeeded or SucceededWithErrors;
        return new InstallOutcome(
            ok,
            (bool)result.RebootRequired,
            ok ? null : $"0x{(int)result.GetUpdateResult(0).HResult:X8}");
    }

    private dynamic Find(AvailableUpdate update) =>
        _session is not null && _updates.TryGetValue(update.Id, out var item)
            ? item
            : throw new InvalidOperationException($"'{update.Title}' wasn't found by the last check.");

    private static UpdateKind KindOf(dynamic update)
    {
        if ((int)update.Type == DriverType)
            return UpdateKind.Driver;
        var categories = update.Categories;
        for (var i = 0; i < (int)categories.Count; i++)
        {
            var category = categories.Item(i);
            if (string.Equals((string)category.CategoryID, UpgradesCategory, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string)category.Name, "Upgrades", StringComparison.OrdinalIgnoreCase))
                return UpdateKind.Feature;
        }
        return UpdateKind.Quality;
    }

    private static dynamic Collection(dynamic update)
    {
        var collection = Create("Microsoft.Update.UpdateColl");
        collection.Add(update);
        return collection;
    }

    private static dynamic Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;
}
