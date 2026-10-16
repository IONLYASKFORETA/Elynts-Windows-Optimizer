using System.Globalization;
using System.Text.Json;

namespace Elynt.Core.Drivers;

/// <summary>
/// NVIDIA's latest Game Ready or Studio driver, from the undocumented AjaxDriverService endpoint the GeForce site uses
/// (research/driver-install-flow.md). The endpoint needs a product-family ID, which NVIDIA only maps from the GPU's
/// name, so the app ships a name-to-family table. Unknown GPUs and failed lookups fall back to NVIDIA's driver page.
/// </summary>
public sealed class NvidiaDriverSource(HttpClient http) : IDriverSource
{
    private const string LookupUrl =
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup";

    /// <summary>NVIDIA's operating-system ID for Windows 11.</summary>
    private const int Windows11 = 135;

    /// <summary>The "International" language, which every GeForce driver package covers.</summary>
    private const int International = 1078;

    private static readonly Lazy<FamilyTable> Families = new(FamilyTable.Load);

    public GpuVendor Vendor => GpuVendor.Nvidia;

    public string VendorPage => "https://www.nvidia.com/en-us/drivers/";

    /// <summary>
    /// The full package runs silently with -s (NVIDIA's documented switch); -noreboot leaves the restart to the user.
    /// It exits 1 when a restart is needed.
    /// </summary>
    public InstallerProfile Installer { get; } = new(
        "NVIDIA Corporation",
        ["-s", "-noreboot"],
        ["-clean"],
        [1, .. InstallerProfile.StandardRebootCodes]);

    public DriverOffer? FindLatest(DriverQuery query)
    {
        if (Families.Value.Find(query.GpuName, query.Laptop) is not { } family)
            return null;

        var url = $"{LookupUrl}&pfid={family}&osID={Windows11}&dch=1&numberOfResults=10&languageCode={International}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // The endpoint answers browsers; a plain client user agent has been refused in the past (TinyNvidiaUpdateChecker).
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        using var response = http.Send(request);
        response.EnsureSuccessStatusCode();
        using var body = response.Content.ReadAsStream();
        using var json = JsonDocument.Parse(body);
        return Pick(json.RootElement, query.Branch);
    }

    private static DriverOffer? Pick(JsonElement root, DriverBranch branch)
    {
        if (!root.TryGetProperty("Success", out var success) || Number(success) < 1)
            return null;
        if (!root.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array)
            return null;

        // IsCRD is 1 for Studio ("Creator Ready") drivers and 0 for Game Ready. Results come newest first.
        var wantCrd = branch == DriverBranch.Studio ? 1 : 0;
        foreach (var item in ids.EnumerateArray())
        {
            if (!item.TryGetProperty("downloadInfo", out var info))
                continue;
            if (!info.TryGetProperty("IsCRD", out var crd) || Number(crd) != wantCrd)
                continue;
            if (info.TryGetProperty("IsBeta", out var beta) && Number(beta) == 1)
                continue;

            var version = info.GetProperty("Version").GetString() ?? throw new JsonException("A driver had no version.");
            var download = new Uri(info.GetProperty("DownloadURL").GetString() ?? throw new JsonException("A driver had no download link."));
            if (download.Scheme != Uri.UriSchemeHttps || !download.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"NVIDIA's lookup pointed at a download outside NVIDIA's servers: {download.Host}.");

            return new DriverOffer(version, branch, download, Released(info));
        }

        return null;
    }

    private static DateTimeOffset? Released(JsonElement info) =>
        info.TryGetProperty("ReleaseDateTime", out var date) &&
        DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>The endpoint sends numbers sometimes as numbers and sometimes as strings.</summary>
    private static int Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetInt32(),
        JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
        _ => -1,
    };

    /// <summary>GPU name to NVIDIA product-family ID, separately for desktop and notebook GPUs.</summary>
    private sealed class FamilyTable(Dictionary<string, string> desktop, Dictionary<string, string> notebook)
    {
        public static FamilyTable Load()
        {
            using var stream = typeof(NvidiaDriverSource).Assembly.GetManifestResourceStream("Elynt.Core.Drivers.nvidia-gpu-data.json")
                ?? throw new InvalidOperationException("The NVIDIA GPU table is missing from the build.");
            var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                ?? throw new InvalidDataException("The NVIDIA GPU table is empty.");
            return new FamilyTable(
                new Dictionary<string, string>(data["desktop"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(data["notebook"], StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Windows names the GPU "NVIDIA GeForce RTX 4070"; the table drops the "NVIDIA " prefix. A laptop with a
        /// desktop GPU (an eGPU) still matches through the other table.
        /// </summary>
        public string? Find(string gpuName, bool laptop)
        {
            var name = gpuName.Trim();
            if (name.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase))
                name = name["NVIDIA ".Length..].Trim();
            var (first, second) = laptop ? (notebook, desktop) : (desktop, notebook);
            return first.GetValueOrDefault(name) ?? second.GetValueOrDefault(name);
        }
    }
}
