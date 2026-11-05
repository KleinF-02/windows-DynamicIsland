using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace DynamicIsland.App.Services;

public sealed record AgentFileContent(
    string FileName,
    string Content,
    long Bytes,
    bool IsTruncated,
    string Kind,
    string? ImageBase64 = null,
    string? ImageMimeType = null)
{
    public bool IsImage => !string.IsNullOrWhiteSpace(ImageBase64);
}

/// <summary>
/// Reads local files for the AI Agent. Text/code, images, Word, Excel and
/// PowerPoint are supported; binary formats without a text extractor are rejected.
/// </summary>
public static class AgentFileReader
{
    public const long MaxFileBytes = 16 * 1024 * 1024;

    private const long MaxTextBytes = 4 * 1024 * 1024;
    private const long MaxImageBytes = 12 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonl", ".csv", ".tsv", ".log",
        ".xml", ".xaml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml",
        ".cs", ".ps1", ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".htm",
        ".css", ".scss", ".sql", ".java", ".c", ".h", ".cpp", ".hpp", ".go",
        ".rs", ".rb", ".php", ".sh", ".bat", ".cmd", ".tex", ".properties",
        ".gitignore", ".editorconfig"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx"
    };

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return TextExtensions.Contains(extension)
               || ImageExtensions.Contains(extension)
               || OfficeExtensions.Contains(extension);
    }

    public static string BuildDialogFilter()
    {
        return "支持的文件|*.txt;*.md;*.markdown;*.json;*.jsonl;*.csv;*.tsv;*.log;*.xml;*.xaml;*.yaml;*.yml;*.ini;*.cfg;*.conf;*.toml;*.cs;*.ps1;*.py;*.js;*.jsx;*.ts;*.tsx;*.html;*.htm;*.css;*.scss;*.sql;*.java;*.c;*.h;*.cpp;*.hpp;*.go;*.rs;*.rb;*.php;*.sh;*.bat;*.cmd;*.tex;*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.docx;*.xlsx;*.pptx|"
               + "图片|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|"
               + "Office 文档|*.docx;*.xlsx;*.pptx|"
               + "文本与代码|*.txt;*.md;*.json;*.csv;*.log;*.xml;*.cs;*.ps1;*.py;*.js;*.ts;*.html;*.css|"
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

        var extension = info.Extension;
        if (ImageExtensions.Contains(extension))
        {
            if (info.Length > MaxImageBytes)
            {
                throw new InvalidDataException($"图片超过 {MaxImageBytes / 1024 / 1024} MB 限制。");
            }

            return await ReadImageAsync(path, info, cancellationToken);
        }

        if (OfficeExtensions.Contains(extension))
        {
            if (info.Length > MaxFileBytes)
            {
                throw new InvalidDataException($"文档超过 {MaxFileBytes / 1024 / 1024} MB 限制。");
            }

            var officeText = extension.ToLowerInvariant() switch
            {
                ".docx" => await ReadDocxAsync(path, cancellationToken),
                ".xlsx" => await ReadXlsxAsync(path, maxCharacters, cancellationToken),
                ".pptx" => await ReadPptxAsync(path, cancellationToken),
                _ => throw new NotSupportedException("暂不支持该 Office 文件类型。")
            };

            return BuildTextResult(info, officeText, maxCharacters, extension.TrimStart('.').ToUpperInvariant());
        }

        if (!TextExtensions.Contains(extension))
        {
            throw new NotSupportedException("暂不支持该文件类型，请使用文本、代码、图片或 Office 文件。");
        }

        if (info.Length > MaxTextBytes)
        {
            throw new InvalidDataException($"文本文件超过 {MaxTextBytes / 1024 / 1024} MB 限制。");
        }

        var text = await ReadTextAsync(path, cancellationToken);
        return BuildTextResult(info, text, maxCharacters, extension.TrimStart('.').ToUpperInvariant());
    }

    private static AgentFileContent BuildTextResult(
        FileInfo info,
        string text,
        int maxCharacters,
        string kind)
    {
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

    private static async Task<AgentFileContent> ReadImageAsync(
        string path,
        FileInfo info,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var normalized = NormalizeImage(bytes);
        return new AgentFileContent(
            info.Name,
            string.Empty,
            info.Length,
            IsTruncated: false,
            Kind: "图片",
            ImageBase64: normalized,
            ImageMimeType: "image/png");
    }

    private static string NormalizeImage(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        BitmapSource source = frame;
        const int maxEdge = 1600;
        var longestEdge = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longestEdge > maxEdge)
        {
            var scale = (double)maxEdge / longestEdge;
            var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            resized.Freeze();
            source = resized;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return Convert.ToBase64String(output.ToArray());
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

        // Check BOMs before the NUL-byte heuristic: UTF-16 text is full of NUL
        // bytes but is perfectly valid text.
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
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("不是有效的 .docx 文件。");

        using var documentStream = entry.Open();
        var document = await XDocument.LoadAsync(documentStream, LoadOptions.None, cancellationToken);
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

    private static async Task<string> ReadXlsxAsync(
        string path,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sharedStrings = new List<string>();
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry is not null)
        {
            using var stream = sharedEntry.Open();
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            foreach (var item in document.Descendants(spreadsheet + "si"))
            {
                sharedStrings.Add(string.Concat(item.Descendants(spreadsheet + "t").Select(node => node.Value)));
            }
        }

        var builder = new StringBuilder();
        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                     && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = entry.Open();
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            builder.AppendLine($"===== {Path.GetFileName(entry.FullName)} =====");

            foreach (var row in document.Descendants(spreadsheet + "row"))
            {
                var values = new List<string>();
                foreach (var cell in row.Elements(spreadsheet + "c"))
                {
                    var type = (string?)cell.Attribute("t");
                    string value;
                    if (type == "inlineStr")
                    {
                        value = string.Concat(cell.Descendants(spreadsheet + "t").Select(node => node.Value));
                    }
                    else
                    {
                        var raw = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
                        if (type == "s" && int.TryParse(raw, out var index)
                                          && index >= 0 && index < sharedStrings.Count)
                        {
                            value = sharedStrings[index];
                        }
                        else
                        {
                            value = raw;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Replace('\n', ' ').Replace('\r', ' '));
                    }
                }

                if (values.Count > 0)
                {
                    builder.AppendLine(string.Join("\t", values));
                }

                if (builder.Length > maxCharacters * 4)
                {
                    builder.AppendLine("…[表格内容过长，已提前停止读取]");
                    return builder.ToString();
                }
            }
        }

        return builder.ToString();
    }

    private static async Task<string> ReadPptxAsync(string path, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var builder = new StringBuilder();
        var slides = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        var index = 0;
        foreach (var entry in slides)
        {
            index++;
            using var stream = entry.Open();
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var text = string.Join(
                "\n",
                document.Descendants(drawing + "p")
                    .Select(paragraph => string.Concat(paragraph.Descendants(drawing + "t").Select(node => node.Value)))
                    .Where(paragraph => paragraph.Length > 0));

            if (text.Length > 0)
            {
                builder.AppendLine($"===== 第 {index} 页 =====");
                builder.AppendLine(text);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
