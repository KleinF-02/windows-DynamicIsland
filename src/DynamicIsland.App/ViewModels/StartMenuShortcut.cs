using System.Windows.Media;

namespace DynamicIsland.App.ViewModels;

public sealed class StartMenuShortcut
{
    public StartMenuShortcut(string name, string path, string folder, ImageSource? icon)
    {
        Name = name;
        Path = path;
        Folder = folder;
        Icon = icon;
    }

    public string Name { get; }

    public string Path { get; }

    public string Folder { get; }

    public ImageSource? Icon { get; }
}
