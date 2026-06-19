using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WowVmMonitor.Infrastructure.Credentials;

public sealed class WindowsCredentialNativeApi : ICredentialNativeApi
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;

    public const int MaximumPasswordCharacters = MaximumCredentialBlobBytes / sizeof(char);

    public void Write(string target, string username, ReadOnlySpan<char> password)
    {
        EnsureWindows();
        var blob = MemoryMarshal.AsBytes(password).ToArray();
        var handle = default(GCHandle);
        try
        {
            handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = CredentialPersistLocalMachine,
                UserName = username
            };

            if (!CredWrite(ref credential, 0))
            {
                throw CreateException("write", target);
            }
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            Array.Clear(blob);
        }
    }

    public ShareCredential? Read(string target)
    {
        EnsureWindows();
        if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new CredentialStoreException("read", target, error);
        }

        byte[]? blob = null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            blob = new byte[credential.CredentialBlobSize];
            if (blob.Length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            }

            var password = MemoryMarshal.Cast<byte, char>(blob).ToArray();
            return new ShareCredential(credential.UserName ?? string.Empty, password);
        }
        finally
        {
            if (blob is not null)
            {
                Array.Clear(blob);
            }

            CredFree(credentialPointer);
        }
    }

    public bool Delete(string target)
    {
        EnsureWindows();
        if (CredDelete(target, CredentialTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new CredentialStoreException("delete", target, error);
    }

    private static CredentialStoreException CreateException(string operation, string target) =>
        new(operation, target, Marshal.GetLastWin32Error());

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager requires Windows.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out nint credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
