using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.App.Interop;

namespace DynamicIsland.App.Services;

/// <summary>
/// Extracts the shell icon for a shortcut/executable. Using the shell instead of
/// Icon.ExtractAssociatedIcon also handles .lnk and .url files correctly.
/// </summary>
public static class ShellIconService
{
    public static ImageSource? TryGetIcon(string path)
    {
        var fileInfo = new NativeMethods.ShFileInfo();
        try
        {
            var result = NativeMethods.SHGetFileInfo(
                path,
                0,
                ref fileInfo,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);

            if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (fileInfo.IconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(fileInfo.IconHandle);
            }
        }
    }
}
