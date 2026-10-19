using System.Xml.Linq;

namespace Elynt.Core.Drivers;

/// <summary>Unpacks a self-extracting driver package (NVIDIA's is a 7-Zip SFX) into a folder.</summary>
public interface IPackageExtractor
{
    /// <summary>Extracts <paramref name="archive"/> into <paramref name="destination"/>. Throws on failure.</summary>
    void Extract(string archive, string destination);
}

/// <summary>What a lean NVIDIA package kept and removed.</summary>
public sealed record LeanPackage(IReadOnlyList<string> Kept, IReadOnlyList<string> Removed);

/// <summary>
/// Strips an extracted NVIDIA driver package down to the display driver and what it needs. Our own implementation of
/// the technique described in research/driver-install-flow.md section 4; no third-party code.
/// <list type="bullet">
/// <item>Components are the package's subfolders that hold a <c>.nvi</c> manifest. Kept: <c>Display.Driver</c>,
/// on laptops also <c>NVPCF</c> (Dynamic Boost) and <c>PPC</c>, plus everything those require, followed through the
/// manifests' <c>requires</c> dependencies. Every other component is deleted.</item>
/// <item>NVIDIA's telemetry components (<c>NvTelemetry*</c>) are always deleted, even when a kept component's manifest
/// says it requires one: the display driver installs without them (research/driver-install-flow.md lists NvTelemetry
/// as dropped), and the app sends no telemetry.</item>
/// <item>Folders without a manifest (<c>NVI2</c>, the installer's own engine) and top-level files are kept.</item>
/// <item>The EULA, functional-consent and privacy-policy entries are removed from <c>setup.cfg</c>, without which
/// setup fails once their components are gone, and the installer's telemetry/advert URLs in
/// <c>NVI2/presentations.cfg</c> are blanked.</item>
/// </list>
/// </summary>
public static class LeanNvidiaPackage
{
    public const string DisplayDriver = "Display.Driver";

    /// <summary>Laptop-only components that must stay: Dynamic Boost and the USB-C power controller.</summary>
    public static IReadOnlyList<string> LaptopComponents { get; } = ["NVPCF", "PPC"];

    /// <summary>Components never kept, whatever requires them.</summary>
    private const string TelemetryPrefix = "NvTelemetry";

    private static readonly string[] ConsentFiles = ["${{EulaHtmlFile}}", "${{FunctionalConsentFile}}", "${{PrivacyPolicyFile}}"];
    private static readonly string[] PresentationUrls = ["ProgressPresentationUrl", "ProgressPresentationSelectedPackageUrl"];

    /// <summary>Strips the package in <paramref name="root"/> in place. Throws <see cref="InvalidDataException"/> when it isn't an NVIDIA driver package.</summary>
    public static LeanPackage Strip(string root, bool laptop)
    {
        var setupExe = Path.Combine(root, "setup.exe");
        var setupCfg = Path.Combine(root, "setup.cfg");
        if (!File.Exists(setupExe) || !File.Exists(setupCfg))
            throw new InvalidDataException("The package has no setup.exe and setup.cfg.");
        var setup = XDocument.Load(setupCfg, LoadOptions.PreserveWhitespace);
        if (setup.Root?.Name.LocalName != "setup")
            throw new InvalidDataException("The package's setup.cfg isn't NVIDIA's setup file.");

        var components = Directory.GetDirectories(root)
            .Select(ReadComponent)
            .OfType<Component>()
            .ToList();
        if (Resolve(components, DisplayDriver) is null)
            throw new InvalidDataException("The package has no display driver component.");

        var wanted = new List<string> { DisplayDriver };
        if (laptop)
            wanted.AddRange(LaptopComponents);
        var kept = new HashSet<Component>();
        var pending = new Queue<Component>(wanted.Select(w => Resolve(components, w)).OfType<Component>());
        while (pending.TryDequeue(out var component))
        {
            if (IsTelemetry(component) || !kept.Add(component))
                continue;
            foreach (var dependency in component.Requires)
                if (Resolve(components, dependency) is { } required)
                    pending.Enqueue(required);
        }

        var removed = components.Where(c => !kept.Contains(c)).ToList();
        foreach (var component in removed)
            Directory.Delete(component.Folder, recursive: true);

        foreach (var file in setup.Descendants().Where(e => e.Name.LocalName == "file"
                     && ConsentFiles.Contains((string?)e.Attribute("name"))).ToList())
            file.Remove();
        setup.Save(setupCfg, SaveOptions.DisableFormatting);

        BlankPresentationUrls(Path.Combine(root, "NVI2", "presentations.cfg"));

        return new LeanPackage(
            kept.Select(c => Path.GetFileName(c.Folder)).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            removed.Select(c => Path.GetFileName(c.Folder)).Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void BlankPresentationUrls(string path)
    {
        if (!File.Exists(path))
            return;
        var presentations = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        foreach (var url in presentations.Descendants().Where(e => PresentationUrls.Contains((string?)e.Attribute("name"))))
            url.SetAttributeValue("value", "");
        presentations.Save(path, SaveOptions.DisableFormatting);
    }

    private static bool IsTelemetry(Component component) =>
        component.Name.StartsWith(TelemetryPrefix, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(component.Folder).StartsWith(TelemetryPrefix, StringComparison.OrdinalIgnoreCase);

    private sealed record Component(string Folder, string Name, IReadOnlyList<string> Requires);

    /// <summary>The component in this folder, or null when the folder has no readable .nvi manifest.</summary>
    private static Component? ReadComponent(string folder)
    {
        var folderName = Path.GetFileName(folder);
        var manifest = Directory.GetFiles(folder, "*.nvi")
            .OrderBy(f => string.Equals(Path.GetFileNameWithoutExtension(f), folderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (manifest is null)
            return null;
        XDocument nvi;
        try
        {
            nvi = XDocument.Load(manifest);
        }
        catch (System.Xml.XmlException)
        {
            // An unreadable manifest: keep the folder rather than guess what it is.
            return null;
        }

        var requires = nvi.Descendants()
            .Where(e => e.Name.LocalName == "package" && e.Parent?.Name.LocalName == "dependencies"
                && string.Equals((string?)e.Attribute("type"), "requires", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("package"))
            .OfType<string>()
            .ToList();
        return new Component(folder, (string?)nvi.Root?.Attribute("name") ?? folderName, requires);
    }

    /// <summary>
    /// The component a package name refers to: by the name its manifest declares, then by folder name, then the
    /// folder whose name is the longest dotted prefix of it (NvContainer.LocalSystem lives in NvContainer).
    /// </summary>
    private static Component? Resolve(IReadOnlyList<Component> components, string name)
    {
        bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return components.FirstOrDefault(c => Is(c.Name, name))
            ?? components.FirstOrDefault(c => Is(Path.GetFileName(c.Folder), name))
            ?? components
                .Where(c => name.StartsWith(Path.GetFileName(c.Folder) + ".", StringComparison.OrdinalIgnoreCase))
                .MaxBy(c => Path.GetFileName(c.Folder).Length);
    }
}
