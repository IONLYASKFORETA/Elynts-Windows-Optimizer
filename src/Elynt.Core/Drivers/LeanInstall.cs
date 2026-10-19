using Microsoft.Extensions.Logging;

namespace Elynt.Core.Drivers;

/// <summary>The lean-install step of the driver flow: runs after the download is verified and before setup.</summary>
internal static class LeanInstall
{
    /// <summary>
    /// Unpacks the verified package into <paramref name="folder"/>, checks the unpacked setup.exe is still the
    /// vendor's, and strips the package. Returns why the lean install can't go ahead, or null when
    /// <paramref name="folder"/>/setup.exe is ready to run.
    /// </summary>
    public static string? Prepare(DriverAdapters adapters, string package, string folder, bool laptop, InstallerProfile profile, ILogger log)
    {
        if (adapters.Extractor is not { } extractor)
            return "Lean install isn't available on this PC.";

        try
        {
            extractor.Extract(package, folder);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Unpacking {Package} for a lean install failed", package);
            return $"Couldn't unpack the driver for a lean install: {ex.Message}";
        }

        var setup = Path.Combine(folder, "setup.exe");
        try
        {
            if (!File.Exists(setup))
                return "The unpacked driver has no setup.exe.";
            var signature = adapters.Signatures.Check(setup);
            if (!signature.Trusted || !string.Equals(signature.Signer?.Trim(), profile.Signer, StringComparison.OrdinalIgnoreCase))
                return $"The unpacked setup.exe isn't signed by {profile.Signer}.";
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Checking the signature of {Setup} failed", setup);
            return $"Couldn't check the unpacked setup.exe's signature: {ex.Message}";
        }

        try
        {
            var lean = LeanNvidiaPackage.Strip(folder, laptop);
            log.LogInformation("Lean install keeps {Kept} and removes {Removed}", lean.Kept, lean.Removed);
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            log.LogWarning(ex, "Stripping the driver package in {Folder} failed", folder);
            return $"Couldn't strip the driver package: {ex.Message}";
        }
    }

    public static void TryDeleteFolder(string folder, ILogger log)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete the unpacked driver {Folder}", folder);
        }
    }
}
