using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using IDataObject = System.Windows.IDataObject;

namespace DynamicIsland.App.Services;

/// <summary>
/// Handles the FileGroupDescriptorW + FileContents pair used by some shell/Start
/// menu drag sources. The descriptor carries names/sizes, while FileContents is
/// the concatenated file payload; shortcuts are materialized under LocalAppData.
/// </summary>
public static class ShellFileGroupParser
{
    private const string UnicodeDescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    public static bool HasPayload(IDataObject data)
        => data.GetDataPresent(UnicodeDescriptorFormat)
           || data.GetDataPresent(ContentsFormat);

    public static IReadOnlyList<string> Extract(IDataObject data)
    {
        if (!data.GetDataPresent(UnicodeDescriptorFormat))
        {
            return [];
        }

        var descriptorBytes = ToBytes(data.GetData(UnicodeDescriptorFormat));
        if (descriptorBytes is null || descriptorBytes.Length < 4)
        {
            return [];
        }

        var descriptors = ReadDescriptors(descriptorBytes);
        if (descriptors.Count == 0)
        {
            return [];
        }

        var contents = ToBytes(data.GetData(ContentsFormat));
        if (contents is null || contents.Length == 0)
        {
            return [];
        }

        var result = new List<string>();
        var offset = 0;
        var outputRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIsland",
            "shortcuts");

        foreach (var descriptor in descriptors)
        {
            if (descriptor.Size <= 0 || offset + descriptor.Size > contents.Length)
            {
                break;
            }

            var outputDirectory = Path.Combine(outputRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);

            var fileName = SanitizeFileName(descriptor.Name);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                fileName += ".lnk";
            }

            var outputPath = Path.Combine(outputDirectory, fileName);
            var startOffset = offset;
            offset += descriptor.Size;

            try
            {
                File.WriteAllBytes(outputPath, contents.AsSpan(startOffset, descriptor.Size).ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (ShortcutPath.TryNormalize(outputPath, out var normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static IReadOnlyList<Descriptor> ReadDescriptors(byte[] bytes)
    {
        var count = BitConverter.ToInt32(bytes, 0);
        if (count <= 0 || count > 64)
        {
            return [];
        }

        var descriptorSize = Marshal.SizeOf<FileDescriptorW>();
        if (descriptorSize <= 0 || 4 + (count * descriptorSize) > bytes.Length)
        {
            return [];
        }

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var root = handle.AddrOfPinnedObject();
            var descriptors = new List<Descriptor>(count);
            for (var i = 0; i < count; i++)
            {
                var descriptor = Marshal.PtrToStructure<FileDescriptorW>(
                    IntPtr.Add(root, 4 + (i * descriptorSize)));
                var size = ((long)descriptor.FileSizeHigh << 32) | descriptor.FileSizeLow;
                if (size > int.MaxValue)
                {
                    continue;
                }

                descriptors.Add(new Descriptor(
                    descriptor.FileName ?? string.Empty,
                    (int)size));
            }

            return descriptors;
        }
        finally
        {
            handle.Free();
        }
    }

    private static string SanitizeFileName(string name)
    {
        var fileName = Path.GetFileName(name.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(fileName) ? "shortcut.lnk" : fileName;
    }

    private static byte[]? ToBytes(object? payload)
    {
        switch (payload)
        {
            case byte[] bytes:
                return bytes;
            case MemoryStream stream:
                return stream.ToArray();
            case Stream stream when stream.CanRead:
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            case IStream stream:
                return ReadComStream(stream);
            default:
                return null;
        }
    }

    private static byte[]? ReadComStream(IStream stream)
    {
        try
        {
            stream.Stat(out var stat, 1);
            if (stat.cbSize <= 0 || stat.cbSize > 64 * 1024 * 1024)
            {
                return null;
            }

            var length = checked((int)stat.cbSize);
            var bytes = new byte[length];
            stream.Seek(0, 0, IntPtr.Zero);
            stream.Read(bytes, length, IntPtr.Zero);
            return bytes;
        }
        catch (Exception ex) when (ex is COMException or IOException or OverflowException)
        {
            return null;
        }
    }

    private sealed record Descriptor(string Name, int Size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileDescriptorW
    {
        internal uint Flags;
        internal Guid ClassId;
        internal int Width;
        internal int Height;
        internal int PointX;
        internal int PointY;
        internal uint FileAttributes;
        internal FileTime CreationTime;
        internal FileTime LastAccessTime;
        internal FileTime LastWriteTime;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint Low;
        internal uint High;
    }
}
