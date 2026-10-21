using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

public sealed class FakeDriverInventory : IDriverInventory
{
    public IReadOnlyList<DisplayAdapter> Adapters { get; set; } = [];

    public Dictionary<GpuVendor, string> ChipsetVersions { get; } = [];

    public IReadOnlyList<DisplayAdapter> DisplayAdapters() => Adapters;

    public string? ChipsetVersion(GpuVendor vendor) => ChipsetVersions.GetValueOrDefault(vendor);
}

/// <summary>A driver lookup; NVIDIA's GPU lookup unless told otherwise.</summary>
public sealed class FakeDriverSource(
    GpuVendor vendor = GpuVendor.Nvidia,
    DriverKind kind = DriverKind.Graphics,
    string vendorPage = "https://www.nvidia.com/en-us/drivers/",
    InstallerProfile? installer = null) : IDriverSource
{
    public GpuVendor Vendor => vendor;
    public DriverKind Kind => kind;
    public string VendorPage => vendorPage;
    public InstallerProfile Installer { get; } = installer ?? new("NVIDIA Corporation", ["-s", "-noreboot"], ["-clean"], [1, 3010, 1641]);
    public Dictionary<DriverBranch, DriverOffer> Offers { get; } = [];
    public Exception? Fail { get; set; }
    public DriverQuery? LastQuery { get; private set; }

    public DriverOffer? FindLatest(DriverQuery query)
    {
        LastQuery = query;
        if (Fail is not null)
            throw Fail;
        return Offers.GetValueOrDefault(query.Branch);
    }
}

public sealed class FakeDownloader : IDownloader
{
    public byte[] Content { get; set; } = [];
    public Exception? Fail { get; set; }
    public Uri? LastUrl { get; private set; }
    public Uri? LastReferer { get; private set; }

    public void Download(Uri url, string path, Uri? referer = null)
    {
        LastUrl = url;
        LastReferer = referer;
        if (Fail is not null)
            throw Fail;
        File.WriteAllBytes(path, Content);
    }
}

/// <summary>Says every file is validly signed by NVIDIA unless told otherwise.</summary>
public sealed class FakeSignatureVerifier : ISignatureVerifier
{
    public SignatureCheck Result { get; set; } = new(Trusted: true, Signer: "NVIDIA Corporation");

    public SignatureCheck Check(string path) => Result;
}

public sealed class FakeInstallerRunner : IInstallerRunner
{
    public sealed record Run(byte[] Content, IReadOnlyList<string> Arguments);

    public int ExitCode { get; set; }
    public List<Run> Runs { get; } = [];

    public int RunInstaller(string path, IReadOnlyList<string> arguments)
    {
        Runs.Add(new Run(File.ReadAllBytes(path), arguments));
        return ExitCode;
    }
}
