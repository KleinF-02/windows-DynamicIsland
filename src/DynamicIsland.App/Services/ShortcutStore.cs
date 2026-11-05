using System.IO;
using System.Text.Json;

namespace DynamicIsland.App.Services;

/// <summary>
/// Persists exactly six shortcut paths. Empty slots are stored as JSON nulls.
/// </summary>
public sealed class ShortcutStore
{
    public const int SlotCount = 6;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ShortcutStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DynamicIsland",
                "shortcuts.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public IReadOnlyList<string?> Load()
    {
        if (!File.Exists(FilePath))
        {
            return Enumerable.Repeat<string?>(null, SlotCount).ToArray();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var saved = JsonSerializer.Deserialize<List<string?>>(json, _jsonOptions) ?? [];
            var slots = Enumerable.Repeat<string?>(null, SlotCount).ToArray();

            for (var i = 0; i < Math.Min(SlotCount, saved.Count); i++)
            {
                if (ShortcutPath.TryNormalize(saved[i], out var path))
                {
                    slots[i] = path;
                }
            }

            return slots;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Enumerable.Repeat<string?>(null, SlotCount).ToArray();
        }
    }

    public bool TrySave(IEnumerable<string?> paths, out string? error)
    {
        var directory = Path.GetDirectoryName(FilePath);
        var temporaryPath = FilePath + ".tmp";

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var slots = paths.Take(SlotCount).ToArray();
            var json = JsonSerializer.Serialize(slots, _jsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex.Message;

            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup only.
            }

            return false;
        }
    }
}
