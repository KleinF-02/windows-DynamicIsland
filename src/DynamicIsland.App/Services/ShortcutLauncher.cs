using System.Diagnostics;
using System.IO;

namespace DynamicIsland.App.Services;

public static class ShortcutLauncher
{
    public static void Launch(string path)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        };

        Process.Start(startInfo);
    }
}
