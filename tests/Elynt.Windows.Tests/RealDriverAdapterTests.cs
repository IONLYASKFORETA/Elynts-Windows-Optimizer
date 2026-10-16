using Elynt.Core.Drivers;

namespace Elynt.Windows.Tests;

/// <summary>The real display-adapter read and Authenticode check, on a real Windows PC. Skipped elsewhere.</summary>
public sealed class RealDriverAdapterTests
{
    public RealDriverAdapterTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows.");

    [Fact]
    public void Reads_display_adapters_with_four_part_driver_versions()
    {
        foreach (var adapter in new WindowsDriverInventory().DisplayAdapters())
            Assert.Equal(4, adapter.DriverVersion.Split('.').Length);
    }

    [Fact]
    public void A_microsoft_signed_executable_is_trusted_and_names_its_signer()
    {
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.SkipUnless(File.Exists(dotnet), "dotnet.exe isn't installed machine-wide.");

        var check = new WinTrustSignatureVerifier().Check(dotnet);

        Assert.True(check.Trusted);
        Assert.Equal("Microsoft Corporation", check.Signer);
    }

    [Fact]
    public void An_unsigned_file_is_not_trusted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elynt-unsigned-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, "MZ not really a program"u8.ToArray());
        try
        {
            var check = new WinTrustSignatureVerifier().Check(path);

            Assert.False(check.Trusted);
            Assert.Null(check.Signer);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
