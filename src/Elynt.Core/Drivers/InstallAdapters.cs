namespace Elynt.Core.Drivers;

/// <summary>Downloads a file over HTTPS.</summary>
public interface IDownloader
{
    /// <summary>Downloads <paramref name="url"/> to <paramref name="path"/>, replacing any file there. Throws on failure.</summary>
    /// <param name="referer">The page the request says it came from, for servers that require one (AMD's).</param>
    void Download(Uri url, string path, Uri? referer = null);
}

/// <summary>The Authenticode verdict on a file.</summary>
/// <param name="Trusted">Windows trusts the signature: it is intact and chains to a trusted root.</param>
/// <param name="Signer">The organisation on the signing certificate, e.g. "NVIDIA Corporation", or null when unsigned.</param>
public sealed record SignatureCheck(bool Trusted, string? Signer);

/// <summary>Checks a file's Authenticode signature (WinVerifyTrust on Windows).</summary>
public interface ISignatureVerifier
{
    SignatureCheck Check(string path);
}

/// <summary>Runs an installer as a child process of the service and waits for it.</summary>
public interface IInstallerRunner
{
    /// <returns>The installer's exit code.</returns>
    int RunInstaller(string path, IReadOnlyList<string> arguments);
}
