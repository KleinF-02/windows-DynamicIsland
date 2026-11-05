namespace DynamicIsland.App.ViewModels;

public sealed class AgentAttachmentViewModel
{
    public AgentAttachmentViewModel(
        string filePath,
        string fileName,
        string content,
        long bytes,
        bool isTruncated,
        string kind)
    {
        FilePath = filePath;
        FileName = fileName;
        Content = content;
        Bytes = bytes;
        IsTruncated = isTruncated;
        Kind = kind;
    }

    public string FilePath { get; }

    public string FileName { get; }

    public string Content { get; }

    public long Bytes { get; }

    public bool IsTruncated { get; }

    public string Kind { get; }

    public string SizeText => Bytes >= 1024 * 1024
        ? $"{Bytes / 1024.0 / 1024.0:0.0} MB"
        : $"{Math.Max(1, Bytes / 1024.0):0.0} KB";

    public string DisplayLabel => $"{Kind} · {FileName} · {SizeText}";

    public string ToolTip => IsTruncated
        ? $"{FilePath}\n内容较长，已截断后发送。"
        : FilePath;
}
