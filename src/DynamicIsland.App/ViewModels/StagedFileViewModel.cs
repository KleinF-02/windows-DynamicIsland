using System.IO;

namespace DynamicIsland.App.ViewModels;

/// <summary>
/// One item held by the file shelf. The shelf keeps a reference to the original
/// path; dragging an item out hands that path to the drop target with a copy effect.
/// </summary>
public sealed class StagedFileViewModel
{
    public StagedFileViewModel(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = GetDisplayName(fullPath);
        KindLabel = GetKindLabel(fullPath, isDirectory);
        SizeText = GetSizeText(fullPath, isDirectory);
    }

    public string FullPath { get; }

    public string Name { get; }

    public string KindLabel { get; }

    public string SizeText { get; }

    public bool IsDirectory { get; }

    public bool Exists => IsDirectory ? Directory.Exists(FullPath) : File.Exists(FullPath);

    private static string GetDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static string GetKindLabel(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return "DIR";
        }

        var extension = Path.GetExtension(path).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "FILE";
        }

        extension = extension.ToUpperInvariant();
        return extension.Length <= 4 ? extension : extension[..4];
    }

    private static string GetSizeText(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return "文件夹";
        }

        try
        {
            return FormatBytes(new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "—";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
