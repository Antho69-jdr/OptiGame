using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace OptiGame.Platform.Drivers;

/// <summary>Signature Authenticode d'un fichier : validité (WinVerifyTrust, chaîne et révocation) et signataire.</summary>
public sealed record SignatureCheck(bool IsValid, string? Signer, string Detail);

/// <summary>
/// Vérifie la signature intégrée d'un exécutable avant de l'ouvrir. WinVerifyTrust contrôle l'intégrité du fichier et la
/// chaîne de certificats (révocation comprise) ; le signataire est ensuite comparé par Core (InstallerSignature).
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static SignatureCheck Verify(string path)
    {
        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 1,   // WTD_REVOKE_WHOLECHAIN
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 0,         // WTD_STATEACTION_IGNORE
                dwProvFlags = 0x80,        // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (result != 0)
            {
                return new SignatureCheck(false, null, $"Signature refusée par Windows (code 0x{result:X8}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }

        try
        {
#pragma warning disable SYSLIB0057 // Lecture du certificat signataire d'un exécutable : pas d'équivalent dans X509CertificateLoader.
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return new SignatureCheck(true, certificate.Subject, "Signature valide.");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return new SignatureCheck(false, null, $"Signataire illisible : {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);
}
