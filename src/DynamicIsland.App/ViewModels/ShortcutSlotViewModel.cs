using System.Windows.Media;
using DynamicIsland.App.Services;

namespace DynamicIsland.App.ViewModels;

public sealed class ShortcutSlotViewModel : ObservableObject
{
    private string? _path;
    private string _displayName = string.Empty;
    private ImageSource? _icon;

    public string? Path => _path;

    public string DisplayName => _displayName;

    public ImageSource? Icon => _icon;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(_path);

    public bool IsEmpty => !HasShortcut;

    public string Initial => string.IsNullOrEmpty(_displayName)
        ? "+"
        : _displayName[..1].ToUpperInvariant();

    public bool IsHtml => HasShortcut
                          && (string.Equals(System.IO.Path.GetExtension(_path), ".html", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(System.IO.Path.GetExtension(_path), ".htm", StringComparison.OrdinalIgnoreCase));

    public string IconGlyph => IsHtml ? "</>" : Initial;

    public string ToolTipText => HasShortcut
        ? $"{_displayName}\n{_path}\n单击打开"
        : "把 .lnk、.exe、.url、.appref-ms 或 .html 拖到这里";

    public bool TrySetShortcut(string? candidate)
    {
        if (!ShortcutPath.TryNormalize(candidate, out var path))
        {
            return false;
        }

        _path = path;
        _displayName = ShortcutPath.DisplayName(path);
        _icon = ShellIconService.TryGetIcon(path);
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasShortcut));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(IsHtml));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(ToolTipText));
        return true;
    }

    public void Clear()
    {
        _path = null;
        _displayName = string.Empty;
        _icon = null;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasShortcut));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(IsHtml));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
