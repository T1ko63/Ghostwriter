using System.ComponentModel;
using System.Runtime.InteropServices;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Secrets;

namespace Kuroko.Platform.Secrets;

/// <summary>
/// API keys as generic credentials in the Windows Credential Manager ("Kuroko:&lt;provider&gt;"), stored for the current
/// user and protected by Windows. The key is kept as UTF-16 text, the same way <c>cmdkey /generic:... /pass:...</c> does.
/// Log lines name the entry and the Win32 error, never the key.
/// </summary>
public sealed class CredentialKeyStore : IKeyStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;
    private const int MaxBlobBytes = 5 * 512; // CRED_MAX_CREDENTIAL_BLOB_SIZE

    public string? Read(string provider)
    {
        var target = KeyStoreNames.Target(provider);
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credential))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_NOT_FOUND) AppLog.Warn($"Credential Manager: {target} cannot be read ({Describe(error)})");
            return null;
        }

        try
        {
            var native = Marshal.PtrToStructure<CREDENTIAL>(credential);
            if (native.CredentialBlob == 0 || native.CredentialBlobSize == 0) return null;
            var key = Marshal.PtrToStringUni(native.CredentialBlob, (int)native.CredentialBlobSize / 2).Trim();
            return key.Length == 0 ? null : key;
        }
        finally
        {
            CredFree(credential);
        }
    }

    public bool Save(string provider, string key)
    {
        key = key.Trim();
        var bytes = key.Length * 2;
        if (key.Length == 0 || bytes > MaxBlobBytes) return false;

        var target = KeyStoreNames.Target(provider);
        var blob = Marshal.AllocHGlobal(bytes);
        var targetName = Marshal.StringToHGlobalUni(target);
        var userName = Marshal.StringToHGlobalUni("Kuroko");
        try
        {
            Marshal.Copy(key.ToCharArray(), 0, blob, key.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetName,
                CredentialBlobSize = (uint)bytes,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userName,
            };
            if (CredWrite(ref credential, 0)) return true;

            AppLog.Warn($"Credential Manager: {target} cannot be written ({Describe(Marshal.GetLastWin32Error())})");
            return false;
        }
        finally
        {
            // Do not leave the key behind in freed native memory.
            for (var i = 0; i < bytes; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(targetName);
            Marshal.FreeHGlobal(userName);
        }
    }

    public bool Remove(string provider)
    {
        var target = KeyStoreNames.Target(provider);
        if (CredDelete(target, CRED_TYPE_GENERIC, 0)) return true;

        var error = Marshal.GetLastWin32Error();
        if (error == ERROR_NOT_FOUND) return true;
        AppLog.Warn($"Credential Manager: {target} cannot be removed ({Describe(error)})");
        return false;
    }

    private static string Describe(int error) => $"{error}: {new Win32Exception(error).Message}";

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
