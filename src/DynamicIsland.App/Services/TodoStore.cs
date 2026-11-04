using System.IO;
using System.Text.Json;

namespace DynamicIsland.App.Services;

public sealed class TodoRecord
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Small atomic JSON store for the todo tab. Todos are local-only and are deleted
/// when completed.
/// </summary>
public sealed class TodoStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public TodoStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DynamicIsland",
                "todos.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public IReadOnlyList<TodoRecord> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var records = JsonSerializer.Deserialize<List<TodoRecord>>(json, _jsonOptions) ?? [];
            var result = new List<TodoRecord>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                if (result.Count >= 20)
                {
                    break;
                }

                if (record is null)
                {
                    continue;
                }

                var text = (record.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.Length > 120)
                {
                    text = text[..120];
                }

                var id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id;
                if (!ids.Add(id))
                {
                    continue;
                }

                result.Add(new TodoRecord
                {
                    Id = id,
                    Text = text,
                    CreatedAt = record.CreatedAt == default ? DateTime.Now : record.CreatedAt
                });
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public bool TrySave(IEnumerable<TodoRecord> records, out string? error)
    {
        var directory = Path.GetDirectoryName(FilePath);
        var temporaryPath = FilePath + ".tmp";

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(records.Take(20), _jsonOptions);
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
