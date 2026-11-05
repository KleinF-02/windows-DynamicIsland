using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DynamicIsland.App.Interop;
using IDataObject = System.Windows.IDataObject;

namespace DynamicIsland.App.Services;

/// <summary>
/// Extracts filesystem paths from the shell's CFSTR_SHELLIDLIST ("Shell IDList
/// Array") format. Windows Start menu drags usually expose this format instead
/// of FileDrop.
/// </summary>
public static class ShellIdListParser
{
    private static readonly string[] Formats =
    [
        "Shell IDList Array",
        "Shell IdList Array"
    ];

    public static bool HasPayload(IDataObject data)
        => Formats.Any(format => data.GetDataPresent(format));

    public static IReadOnlyList<string> Extract(IDataObject data)
    {
        foreach (var format in Formats)
        {
            if (!data.GetDataPresent(format))
            {
                continue;
            }

            object? payload;
            try
            {
                payload = data.GetData(format);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                continue;
            }

            var bytes = ToBytes(payload);
            if (bytes is null)
            {
                continue;
            }

            var paths = ParseCida(bytes);
            if (paths.Count > 0)
            {
                return paths;
            }
        }

        return [];
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
            default:
                return null;
        }
    }

    private static IReadOnlyList<string> ParseCida(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return [];
        }

        var paths = new List<string>();
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var root = handle.AddrOfPinnedObject();
            var count = Marshal.ReadInt32(root);
            if (count <= 0 || count > 64)
            {
                return paths;
            }

            var parentOffset = Marshal.ReadInt32(root, 4);
            if (!IsValidOffset(parentOffset, bytes.Length))
            {
                return paths;
            }

            var parent = IntPtr.Add(root, parentOffset);
            for (var i = 0; i < count; i++)
            {
                var childOffset = Marshal.ReadInt32(root, 8 + (i * 4));
                if (!IsValidOffset(childOffset, bytes.Length))
                {
                    continue;
                }

                var child = IntPtr.Add(root, childOffset);
                var combined = NativeMethods.ILCombine(parent, child);
                if (combined == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var buffer = new StringBuilder(1024);
                    if (NativeMethods.SHGetPathFromIDListW(combined, buffer))
                    {
                        var path = buffer.ToString();
                        if (File.Exists(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(combined);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return paths;
    }

    private static bool IsValidOffset(int offset, int length)
        => offset > 0 && offset < length - 2;
}
