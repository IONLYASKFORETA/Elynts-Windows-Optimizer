using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Elynt.Core.Drivers;

namespace Elynt.Windows;

/// <summary>
/// Checks a file's Authenticode signature with WinVerifyTrust (no UI, whole-chain revocation check), then reads the
/// organisation from the signing certificate.
/// </summary>
public sealed partial class WinTrustSignatureVerifier : ISignatureVerifier
{
    public SignatureCheck Check(string path) => new(Verify(path), Signer(path));

    private static string? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // There is no loader replacement for reading the signer of a signed file.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var organisation = certificate.SubjectName.EnumerateRelativeDistinguishedNames()
                .FirstOrDefault(rdn => rdn.GetSingleElementType().Value == "2.5.4.10")?.GetSingleElementValue();
            return organisation ?? certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch (Exception)
        {
            return null; // Unsigned.
        }
    }

    private static unsafe bool Verify(string path)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        fixed (char* file = path)
        {
            var fileInfo = new WintrustFileInfo
            {
                cbStruct = (uint)sizeof(WintrustFileInfo),
                pcwszFilePath = file,
            };
            var data = new WintrustData
            {
                cbStruct = (uint)sizeof(WintrustData),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 1, // WTD_REVOKE_WHOLECHAIN
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = &fileInfo,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
            };
            var result = WinVerifyTrust(-1, ref action, ref data);
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(-1, ref action, ref data);
            return result == 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WintrustFileInfo
    {
        public uint cbStruct;
        public char* pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WintrustData
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public WintrustFileInfo* pFile;
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
