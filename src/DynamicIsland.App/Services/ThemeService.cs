using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace DynamicIsland.App.Services;

/// <summary>
/// Swaps the palette dictionary when Windows changes its app theme. The styles
/// in Theme.xaml reference palette entries with DynamicResource, so replacing
/// the palette updates already-open island, settings and picker windows.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string PalettePrefix = "pack://application:,,,/DynamicIsland;component/Themes/Palette.";

    private static bool _initialized;
    private static bool? _appliedLight;
    private static DispatcherTimer? _watcher;

    public static bool IsLightTheme { get; private set; }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // App.xaml resources can still be loading when OnStartup runs; defer the
        // first swap until the merged palette is available.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => RefreshFromSystem(force: true)));

            // Some shell updates do not raise UserPreferenceChanged; a light
            // poll makes the app follow Windows settings reliably.
            _watcher = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _watcher.Tick += (_, _) => RefreshFromSystem();
            _watcher.Start();
        }
        else
        {
            RefreshFromSystem(force: true);
        }
    }

    public static void RefreshFromSystem(bool force = false)
    {
        var overrideTheme = Environment.GetEnvironmentVariable("DYNAMIC_ISLAND_THEME");
        var useOverride = string.Equals(overrideTheme, "light", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(overrideTheme, "dark", StringComparison.OrdinalIgnoreCase);

        if (useOverride && string.Equals(overrideTheme, "light", StringComparison.OrdinalIgnoreCase))
        {
            Apply(light: true, force: force);
            return;
        }

        if (useOverride)
        {
            Apply(light: false, force: force);
            return;
        }

        Apply(light: SystemUsesLightTheme(), force: force);
    }

    private static void Apply(bool light, bool force)
    {
        if (!force && _appliedLight == light)
        {
            return;
        }

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                PalettePrefix + (light ? "Light.xaml" : "Dark.xaml"),
                UriKind.Absolute)
        };

        var dictionaries = application.Resources.MergedDictionaries;
        var paletteIndexes = new List<int>();
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Themes/Palette.", StringComparison.OrdinalIgnoreCase)
                || dictionaries[i].Contains("TextPrimaryColor"))
            {
                paletteIndexes.Add(i);
            }
        }

        if (paletteIndexes.Count > 0)
        {
            var target = paletteIndexes[0];
            dictionaries[target] = palette;

            for (var i = paletteIndexes.Count - 1; i >= 0; i--)
            {
                var index = paletteIndexes[i];
                if (index != target)
                {
                    dictionaries.RemoveAt(index);
                }
            }
        }
        else
        {
            dictionaries.Insert(dictionaries.Count, palette);
        }

        _appliedLight = light;
        IsLightTheme = light;
        AppLogger.Info($"Theme switched to {(light ? "light" : "dark")}.");
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            RefreshFromSystem();
        }
        else
        {
            application.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => RefreshFromSystem()));
        }
    }
}
