using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace dlss_updater.Services
{
    // Checks that a DLL carries a valid Authenticode signature from NVIDIA.
    // The SHA-256 in the manifest only proves the file is the one in the
    // release; this proves NVIDIA built it, so a tampered release can't get
    // a DLL of someone else's into the user's applications.
    internal static class NvidiaSignature
    {
        private const string NvidiaOrganization = "NVIDIA Corporation";

        private static readonly Guid GenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_IGNORE = 0;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
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

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            ref WINTRUST_DATA pWVTData);

        public static bool IsSignedByNvidia(string path)
        {
            return HasValidSignature(path) && IsNvidiaSigner(GetSignerSubject(path));
        }

        // The signer's subject must name NVIDIA Corporation as both the
        // common name and the organization.
        internal static bool IsNvidiaSigner(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return false;
            }

            X500DistinguishedName name = new X500DistinguishedName(subject);
            bool commonName = false;
            bool organization = false;

            foreach (string part in name.Decode(X500DistinguishedNameFlags.UseNewLines)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed == "CN=" + NvidiaOrganization)
                {
                    commonName = true;
                }
                else if (trimmed == "O=" + NvidiaOrganization)
                {
                    organization = true;
                }
            }

            return commonName && organization;
        }

        private static bool HasValidSignature(string path)
        {
            IntPtr fileInfo = IntPtr.Zero;

            try
            {
                WINTRUST_FILE_INFO file = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                    pcwszFilePath = path
                };

                fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(file, fileInfo, false);

                // Offline-friendly: no revocation lookups over the network.
                // An expired certificate is still fine when the signature
                // was timestamped, as NVIDIA's are.
                WINTRUST_DATA data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfo,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
                };

                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (fileInfo != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(fileInfo, typeof(WINTRUST_FILE_INFO));
                    Marshal.FreeHGlobal(fileInfo);
                }
            }
        }

        private static string GetSignerSubject(string path)
        {
            try
            {
                using (X509Certificate2 certificate =
                    new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    return certificate.Subject;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
