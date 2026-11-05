using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.App.Services;

/// <summary>
/// Resolves the target executable behind an App Execution Alias
/// (%LocalAppData%\Microsoft\WindowsApps\*.exe). The alias itself has no icon,
/// so the shell shows a generic executable icon; the package target has the
/// real application icon.
/// </summary>
public static class AppExecutionAlias
{
    private const uint FileReadAttributes = 0x0080;
    private const uint ShareReadWriteDelete = 0x0007;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint IoReparseTagAppExecLink = 0x8000001B;
    private const int MaxReparseData = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool TryGetTarget(string aliasPath, out string targetPath)
    {
        targetPath = string.Empty;

        if (!IsWindowsAppsAlias(aliasPath))
        {
            return false;
        }

        var handle = CreateFileW(
            aliasPath,
            FileReadAttributes,
            ShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle == new IntPtr(-1))
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(MaxReparseData);
        try
        {
            if (!DeviceIoControl(
                    handle,
                    FsctlGetReparsePoint,
                    IntPtr.Zero,
                    0,
                    buffer,
                    MaxReparseData,
                    out var bytesReturned,
                    IntPtr.Zero)
                || bytesReturned < 16)
            {
                return false;
            }

            var tag = (uint)Marshal.ReadInt32(buffer);
            if (tag != IoReparseTagAppExecLink)
            {
                return false;
            }

            var dataLength = (ushort)Marshal.ReadInt16(buffer, 4);
            var end = Math.Min((int)bytesReturned, 8 + dataLength);
            var position = 12; // REPARSE_DATA_BUFFER header + AppExecLink version

            while (position + 2 < end)
            {
                var value = ReadUnicodeString(buffer, end, ref position);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (File.Exists(value))
                {
                    targetPath = value;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(handle);
        }
    }

    public static bool IsWindowsAppsAlias(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");

        return path.StartsWith(windowsApps + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadUnicodeString(IntPtr buffer, int end, ref int position)
    {
        var builder = new StringBuilder();
        while (position + 2 <= end)
        {
            var character = (char)Marshal.ReadInt16(buffer, position);
            position += 2;
            if (character == '\0')
            {
                break;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
