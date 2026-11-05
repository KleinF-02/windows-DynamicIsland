using System.IO;
using System.Linq;

namespace DynamicIsland.App.Services;

/// <summary>
/// Validates the shortcut types that make sense for the six app slots and
/// produces a stable display name from the shortcut file.
/// </summary>
public static class ShortcutPath
{
    private static readonly string[] AllowedExtensions =
    [
        ".lnk",
        ".exe",
        ".url",
        ".appref-ms",
        ".html",
        ".htm"
    ];

    public static bool TryNormalize(string? candidate, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension, StringComparer.Ordinal))
        {
            return false;
        }

        path = fullPath;
        return true;
    }

    public static string DisplayName(string path)
    {
        var fileName = Path.GetFileName(path);
        foreach (var extension in AllowedExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^extension.Length];
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
