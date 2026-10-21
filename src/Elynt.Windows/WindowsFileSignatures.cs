using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Elynt.Core.Startup;

namespace Elynt.Windows;

/// <summary>
/// Authenticode checks with WinVerifyTrust (no revocation lookups, so it never waits on the network). The publisher
/// is the signing certificate's subject name; for an unsigned file it falls back to the file's own company name.
/// Files signed only through a catalog (most of Windows' own) report as unsigned here, since only embedded
/// signatures are checked.
/// </summary>
public sealed partial class WindowsFileSignatures : IFileSignatures
{
    private const uint TrustENoSignature = 0x800B0100;
    private const uint TrustESubjectFormUnknown = 0x800B0003;
    private const uint TrustEProviderUnknown = 0x800B0001;

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public FileSignature Inspect(string path)
    {
        if (!File.Exists(path))
            return new FileSignature(SignatureStatus.Unknown, null);

        var status = Verify(path);
        var publisher = status == SignatureStatus.Unsigned ? null : Signer(path);
        return new FileSignature(status, publisher ?? CompanyName(path));
    }

    private static SignatureStatus Verify(string path)
    {
        var fileInfo = new WintrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            pcwszFilePath = Marshal.StringToCoTaskMemUni(path),
        };
        var fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x1000 | 0x80, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE
            };
            var action = GenericVerifyV2;
            var result = (uint)WinVerifyTrust(-1, ref action, ref data);

            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(-1, ref action, ref data);

            return result switch
            {
                0 => SignatureStatus.Signed,
                TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => SignatureStatus.Unsigned,
                _ => SignatureStatus.Invalid,
            };
        }
        catch (Exception)
        {
            return SignatureStatus.Unknown;
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
            Marshal.FreeCoTaskMem(fileInfoPtr);
        }
    }

    private static string? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // The embedded signer certificate is only read through this API.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? CompanyName(string path)
    {
        try
        {
            var company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            return string.IsNullOrEmpty(company) ? null : company;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public nint pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, ref Guid pgActionID, ref WintrustData pWVTData);
}
