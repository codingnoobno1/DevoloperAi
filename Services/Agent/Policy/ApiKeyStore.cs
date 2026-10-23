using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// Stores provider API keys in Windows Credential Manager (agentsettings.md D8), so the key
    /// survives restarts without living in a plain file or an environment variable. The app is
    /// unpackaged (WindowsPackageType=None), where MAUI SecureStorage is unreliable, so this talks
    /// to advapi32 directly. Keys are never written to logs or the audit file.
    /// </summary>
    public sealed class ApiKeyStore
    {
        private const string Prefix = "SyncroDesktop/apikey/";

        public bool Has(string provider) => Get(provider) != null;

        public string? Get(string provider)
        {
            if (!OperatingSystem.IsWindows()) return null;
            if (!CredRead(Prefix + provider.ToLowerInvariant(), CRED_TYPE_GENERIC, 0, out var ptr)) return null;
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return null;
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally { CredFree(ptr); }
        }

        public bool Set(string provider, string key)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(key)) return false;
            var bytes = Encoding.Unicode.GetBytes(key.Trim());
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = Prefix + provider.ToLowerInvariant(),
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = "syncro",
                };
                return CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length); // wipe before free
                Marshal.FreeHGlobal(blob);
            }
        }

        public bool Clear(string provider) =>
            OperatingSystem.IsWindows() && CredDelete(Prefix + provider.ToLowerInvariant(), CRED_TYPE_GENERIC, 0);

        /// <summary>"gsk_…a1b2" style hint for the UI — never the whole key.</summary>
        public static string Mask(string? key) =>
            string.IsNullOrEmpty(key) ? "" : key.Length <= 8 ? new string('•', key.Length) : key[..4] + new string('•', 20) + key[^4..];

        // ── advapi32 ──
        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);
    }
}
