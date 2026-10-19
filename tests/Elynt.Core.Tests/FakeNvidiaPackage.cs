using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>
/// "Extracts" a pretend NVIDIA driver package laid out like the real one: setup.exe and setup.cfg at the top, one
/// folder per component with a .nvi manifest listing what it requires.
/// </summary>
public sealed class FakePackageExtractor : IPackageExtractor
{
    public const string SetupCfg = """
        <?xml version="1.0" encoding="utf-8"?>
        <setup title="NVIDIA Graphics Driver" version="617.42">
          <manifest>
            <file name="${{EulaHtmlFile}}"/>
            <file name="${{FunctionalConsentFile}}"/>
            <file name="${{PrivacyPolicyFile}}"/>
            <file name="setup.exe"/>
            <file name="ListDevices.txt"/>
          </manifest>
        </setup>
        """;

    public const string PresentationsCfg = """
        <?xml version="1.0" encoding="utf-8"?>
        <nvi name="Presentations">
          <strings>
            <string name="ProgressPresentationUrl" value="https://www.nvidia.com/content/driver-install/ad.html"/>
            <string name="ProgressPresentationSelectedPackageUrl" value="https://www.nvidia.com/content/driver-install/package.html"/>
            <string name="InstallerTitle" value="NVIDIA Installer"/>
          </strings>
        </nvi>
        """;

    /// <summary>Component folder → (the name its .nvi manifest declares, the packages it requires).</summary>
    public Dictionary<string, (string Name, string[] Requires)> Components { get; } = new()
    {
        ["Display.Driver"] = ("Display.Driver", ["PhysX", "NvContainer.LocalSystem"]),
        ["PhysX"] = ("PhysX", []),
        ["NvContainer"] = ("NvContainer", []),
        ["HDAudio"] = ("HDAudio.Driver", []),
        ["NvCpl"] = ("Display.NvApp.NvCPL", []),
        ["NvApp"] = ("Display.NvApp", ["Display.NvApp.NvCPL", "NvBackend"]),
        ["NvBackend"] = ("NvBackend", []),
        ["FrameViewSDK"] = ("FrameViewSDK", []),
        ["NvTelemetry"] = ("NvTelemetry", []),
        ["NVPCF"] = ("NVPCF", []),
        ["PPC"] = ("PPC", []),
    };

    public Exception? Fail { get; set; }
    public string? LastArchive { get; private set; }

    public void Extract(string archive, string destination)
    {
        LastArchive = archive;
        if (Fail is not null)
            throw Fail;

        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "setup.exe"), "pretend NVIDIA setup");
        File.WriteAllText(Path.Combine(destination, "setup.cfg"), SetupCfg);
        File.WriteAllText(Path.Combine(destination, "EULA.txt"), "eula");
        File.WriteAllText(Path.Combine(destination, "ListDevices.txt"), "devices");
        var nvi2 = Directory.CreateDirectory(Path.Combine(destination, "NVI2")).FullName;
        File.WriteAllText(Path.Combine(nvi2, "presentations.cfg"), PresentationsCfg);

        foreach (var (folder, (name, requires)) in Components)
        {
            var dir = Directory.CreateDirectory(Path.Combine(destination, folder)).FullName;
            var dependencies = string.Concat(requires.Select(r => $"""<package type="requires" package="{r}"/>"""));
            File.WriteAllText(Path.Combine(dir, $"{folder}.nvi"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <nvi name="{name}" title="{name}" version="1.0">
                  <dependencies>{dependencies}<package type="optional" package="FrameViewSDK"/></dependencies>
                </nvi>
                """);
            File.WriteAllText(Path.Combine(dir, "payload.bin"), folder);
        }
    }
}

/// <summary>Records what the installer folder held at the moment the installer ran.</summary>
public sealed class PackageRecordingInstallerRunner : IInstallerRunner
{
    public sealed record Run(string Path, IReadOnlyList<string> Arguments, IReadOnlyList<string> Folders, string? SetupCfg, string? PresentationsCfg);

    public int ExitCode { get; set; }
    public List<Run> Runs { get; } = [];

    public int RunInstaller(string path, IReadOnlyList<string> arguments)
    {
        var root = Path.GetDirectoryName(path)!;
        var folders = Directory.GetDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        string? Read(string relative) => File.Exists(Path.Combine(root, relative)) ? File.ReadAllText(Path.Combine(root, relative)) : null;
        Runs.Add(new Run(path, arguments, folders!, Read("setup.cfg"), Read(Path.Combine("NVI2", "presentations.cfg"))));
        return ExitCode;
    }
}
