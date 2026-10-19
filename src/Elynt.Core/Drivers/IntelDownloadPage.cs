using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// Reads an Intel download-center page. Its head carries machine-oriented meta fields (DownloadVersion,
/// RecommendedDownloadUrl) and its body lists each file with the hash Intel publishes for it.
/// </summary>
internal static partial class IntelDownloadPage
{
    /// <summary>The value of a meta field, e.g. "32.0.101.9150 WHQL Certified", or null when the page has none.</summary>
    public static string? Meta(string page, string name)
    {
        foreach (Match tag in MetaTag().Matches(page))
        {
            var attributes = Attribute().Matches(tag.Value).ToDictionary(
                a => a.Groups["name"].Value, a => System.Net.WebUtility.HtmlDecode(a.Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
            if (attributes.TryGetValue("name", out var field) &&
                (string.Equals(field, name, StringComparison.OrdinalIgnoreCase) || string.Equals(field, $"meta-{name}", StringComparison.OrdinalIgnoreCase)) &&
                attributes.TryGetValue("content", out var content))
                return content.Trim();
        }

        return null;
    }

    /// <summary>The version number at the start of DownloadVersion, without "WHQL Certified" and the like.</summary>
    public static string Version(string page) =>
        Meta(page, "DownloadVersion") is { } text && LeadingVersion().Match(text) is { Success: true } match
            ? match.Value
            : throw new InvalidDataException("Intel's download page no longer shows a version the app recognises.");

    /// <summary>The one hash of this kind on the page (SHA512 or SHA256), in hex. Throws when there isn't exactly one.</summary>
    public static string Hash(string page, string algorithm, int hexLength)
    {
        var hashes = new Regex($@"{algorithm}\s*:\s*(?:<[^>]*>\s*)*(?<hash>[0-9a-fA-F]{{{hexLength}}})\b", RegexOptions.IgnoreCase)
            .Matches(page).Select(m => m.Groups["hash"].Value.ToUpperInvariant()).Distinct().ToList();
        return hashes.Count == 1
            ? hashes[0]
            : throw new InvalidDataException($"Intel's download page doesn't show exactly one {algorithm} hash, so the download can't be checked.");
    }

    /// <summary>A download link, refused unless it is HTTPS on Intel's download mirror.</summary>
    public static Uri Download(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "downloadmirror.intel.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Intel's page pointed at a download outside Intel's servers: {uri.Host}.");
        return uri;
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"(?<name>[\w-]+)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"^\d+(\.\d+){1,3}")]
    private static partial Regex LeadingVersion();
}
