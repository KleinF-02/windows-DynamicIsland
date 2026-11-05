using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DynamicIsland.App.Services;

public sealed record AgentFileContent(
    string FileName,
    string Content,
    long Bytes,
    bool IsTruncated,
    string Kind);

/// <summary>
/// Reads local files for the AI Agent. Text/code and .docx are supported;
/// binary formats are rejected instead of being sent as mojibake.
/// </summary>
public static class AgentFileReader
{
    public const long MaxFileBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonl", ".csv", ".tsv", ".log",
        ".xml", ".xaml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml",
        ".cs", ".ps1", ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".htm",
        ".css", ".scss", ".sql", ".java", ".c", ".h", ".cpp", ".hpp", ".go",
        ".rs", ".rb", ".php", ".sh", ".bat", ".cmd", ".tex", ".properties",
        ".gitignore", ".editorconfig"
    };

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return TextExtensions.Contains(extension)
               || string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildDialogFilter()
    {
        return "支持的文件|*.txt;*.md;*.markdown;*.json;*.jsonl;*.csv;*.tsv;*.log;*.xml;*.xaml;*.yaml;*.yml;*.ini;*.cfg;*.conf;*.toml;*.cs;*.ps1;*.py;*.js;*.jsx;*.ts;*.tsx;*.html;*.htm;*.css;*.scss;*.sql;*.java;*.c;*.h;*.cpp;*.hpp;*.go;*.rs;*.rb;*.php;*.sh;*.bat;*.cmd;*.tex;*.docx|"
               + "文本与代码|*.txt;*.md;*.json;*.csv;*.log;*.xml;*.cs;*.ps1;*.py;*.js;*.ts;*.html;*.css|"
               + "Word 文档|*.docx|"
               + "所有文件|*.*";
    }

    public static async Task<AgentFileContent> ReadAsync(
        string path,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("文件不存在。", path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"文件超过 {MaxFileBytes / 1024 / 1024} MB 限制。");
        }

        var extension = info.Extension;
        string text;
        string kind;

        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            text = await ReadDocxAsync(path, cancellationToken);
            kind = "Word";
        }
        else if (TextExtensions.Contains(extension))
        {
            text = await ReadTextAsync(path, cancellationToken);
            kind = extension.TrimStart('.').ToUpperInvariant();
        }
        else
        {
            throw new NotSupportedException("暂不支持该文件类型，请使用文本、代码或 .docx 文件。");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("文件没有可读取的文字内容。");
        }

        var truncated = false;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length > maxCharacters)
        {
            text = text[..maxCharacters] + "\n\n…[内容已截断]";
            truncated = true;
        }

        return new AgentFileContent(info.Name, text, info.Length, truncated, kind);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

        // Check BOMs before the NUL-byte binary heuristic: UTF-16 text is full
        // of NUL bytes but is perfectly valid text.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).Contains((byte)0))
        {
            throw new InvalidDataException("检测到二进制内容，无法作为文本读取。");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<string> ReadDocxAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("不是有效的 .docx 文件。");

        using var documentStream = entry.Open();
        var document = await XDocument.LoadAsync(
            documentStream,
            LoadOptions.None,
            cancellationToken);

        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var builder = new StringBuilder();
        foreach (var paragraph in document.Descendants(word + "p"))
        {
            var text = string.Concat(paragraph.Descendants(word + "t").Select(node => node.Value));
            if (text.Length > 0)
            {
                builder.AppendLine(text);
            }
        }

        return builder.ToString();
    }
}
