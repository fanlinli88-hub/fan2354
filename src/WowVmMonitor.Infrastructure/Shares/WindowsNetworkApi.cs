using System.Runtime.InteropServices;

namespace WowVmMonitor.Infrastructure.Shares;

public sealed class WindowsNetworkApi : IWindowsNetworkApi
{
    private const uint ResourceTypeDisk = 1;

    public int Connect(string sharePath, string username, ReadOnlyMemory<char> password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows network connections require Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sharePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var passwordCharacters = password.ToArray();
        var passwordPointer = Marshal.AllocHGlobal((passwordCharacters.Length + 1) * sizeof(char));
        try
        {
            Marshal.Copy(passwordCharacters, 0, passwordPointer, passwordCharacters.Length);
            Marshal.WriteInt16(passwordPointer, passwordCharacters.Length * sizeof(char), 0);
            var resource = new NativeNetworkResource
            {
                Type = ResourceTypeDisk,
                RemoteName = sharePath
            };
            return WNetAddConnection2(ref resource, passwordPointer, username, 0);
        }
        finally
        {
            Array.Clear(passwordCharacters);
            for (var offset = 0; offset < (password.Length + 1) * sizeof(char); offset += sizeof(short))
            {
                Marshal.WriteInt16(passwordPointer, offset, 0);
            }

            Marshal.FreeHGlobal(passwordPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeNetworkResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NativeNetworkResource networkResource,
        nint password,
        string username,
        uint flags);
}
