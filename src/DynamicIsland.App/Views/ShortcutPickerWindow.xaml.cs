using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.App.Services;
using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Views;

public partial class ShortcutPickerWindow : Window, INotifyPropertyChanged
{
    private readonly List<StartMenuShortcut> _allApps;
    private string _searchText = string.Empty;
    private StartMenuShortcut? _selectedApp;

    public ShortcutPickerWindow()
    {
        InitializeComponent();
        _allApps = LoadStartMenuApps();
        FilteredApps = new ObservableCollection<StartMenuShortcut>(_allApps);
        DataContext = this;
        ApplyFilter();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StartMenuShortcut> FilteredApps { get; }

    public string? SelectedPath { get; private set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearchEmpty));
            ApplyFilter();
        }
    }

    public bool IsSearchEmpty => string.IsNullOrWhiteSpace(SearchText);

    public StartMenuShortcut? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (ReferenceEquals(_selectedApp, value))
            {
                return;
            }

            _selectedApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HintText));
        }
    }

    public bool HasNoMatches => FilteredApps.Count == 0;

    public string HintText => SelectedApp is null
        ? $"共 {_allApps.Count} 个开始菜单应用"
        : $"已选择：{SelectedApp.Name}";

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }

        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allApps
            : _allApps.Where(app =>
                    app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || app.Folder.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FilteredApps.Clear();
        foreach (var app in filtered)
        {
            FilteredApps.Add(app);
        }

        SelectedApp = FilteredApps.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HintText));
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null)
        {
            return;
        }

        SelectedPath = SelectedApp.Path;
        DialogResult = true;
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择网页或快捷方式",
            Filter = "网页文件 (*.html;*.htm)|*.html;*.htm|"
                     + "快捷方式 (*.lnk;*.exe;*.url;*.appref-ms)|*.lnk;*.exe;*.url;*.appref-ms|"
                     + "所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true
            || !ShortcutPath.TryNormalize(dialog.FileName, out var path))
        {
            return;
        }

        SelectedPath = path;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AppsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedApp is not null)
        {
            Add_Click(sender, e);
        }
    }

    private static List<StartMenuShortcut> LoadStartMenuApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<StartMenuShortcut>();

        foreach (var root in roots.Where(Directory.Exists))
        {
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in files)
            {
                if (apps.Count >= 250
                    || !seen.Add(path)
                    || !ShortcutPath.TryNormalize(path, out var normalized))
                {
                    continue;
                }

                var name = ShortcutPath.DisplayName(normalized);
                if (name.Contains("卸载", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var folder = Path.GetFileName(Path.GetDirectoryName(normalized)) ?? string.Empty;
                apps.Add(new StartMenuShortcut(
                    name,
                    normalized,
                    folder,
                    ShellIconService.TryGetIcon(normalized)));
            }
        }

        AddWindowsAppAliases(apps, seen);
        apps.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return apps;
    }

    private static void AddWindowsAppAliases(List<StartMenuShortcut> apps, HashSet<string> seen)
    {
        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");

        if (!Directory.Exists(windowsApps))
        {
            return;
        }

        List<string> aliases;
        try
        {
            aliases = Directory.EnumerateFiles(windowsApps, "*.exe", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var path in aliases)
        {
            if (apps.Count >= 250
                || !seen.Add(path)
                || !ShortcutPath.TryNormalize(path, out var normalized))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(normalized);
            var displayName = fileName.ToLowerInvariant() switch
            {
                "snippingtool" => "Snipping Tool",
                "windowsterminal" => "Windows Terminal",
                "winget" => "winget",
                "wt" => "Windows Terminal",
                _ => AddSpaces(fileName)
            };

            apps.Add(new StartMenuShortcut(
                displayName,
                normalized,
                "应用别名",
                ShellIconService.TryGetIcon(normalized)));
        }
    }

    private static string AddSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' '))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
