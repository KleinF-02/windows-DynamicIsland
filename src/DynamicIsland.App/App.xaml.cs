using System.IO;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.App.Services;
using DynamicIsland.App.Views;
using DynamicIsland.Core.Models;
using DynamicIsland.Core.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DynamicIsland.App;

public partial class App : Application
{
    private const string MutexName = @"Local\DynamicIsland.App.SingleInstance";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private SettingsStore? _settingsStore;
    private StartupService? _startupService;
    private TrayIconService? _trayIcon;
    private IslandWindow? _island;
    private SettingsWindow? _settingsWindow;
    private IslandSettings _settings = new();
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterCrashHandlers();

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!HasArgument(e.Args, "--autostart"))
            {
                MessageBox.Show(
                    "灵动岛已经在运行了。\n请在系统托盘中查看。",
                    "灵动岛",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        ThemeService.Initialize();

        try
        {
            var settingsPath = SettingsStore.DefaultPath;
            var firstRun = !File.Exists(settingsPath);
            _settingsStore = new SettingsStore(settingsPath);
            _settings = _settingsStore.Load();

            if (firstRun && !_settingsStore.TrySave(_settings, out var saveError))
            {
                AppLogger.Error($"Unable to create the default settings file: {saveError}");
            }

            _startupService = new StartupService();
            if (!_startupService.TrySetEnabled(_settings.StartWithWindows, out var startupError)
                && startupError is not null)
            {
                AppLogger.Error($"Unable to synchronize the startup entry: {startupError}");
            }

            _island = new IslandWindow(_settings);
            _island.SettingsRequested += (_, _) => OpenSettings();
            _island.ExitRequested += (_, _) => ExitApplication();
            _island.AgentSettingsChanged += OnAgentSettingsChanged;

            _trayIcon = new TrayIconService(_settings.StartWithWindows);
            _trayIcon.ShowRequested += (_, _) => _island?.ShowFromTray();
            _trayIcon.SettingsRequested += (_, _) => OpenSettings();
            _trayIcon.NotificationRequested += (_, _) => _island?.ShowNotification(
                "通知通道已就绪",
                "媒体播放、日程和充电提醒都可以复用这块区域。",
                TimeSpan.FromSeconds(4));
            _trayIcon.StartWithWindowsChanged += OnTrayStartWithWindowsChanged;
            _trayIcon.ExitRequested += (_, _) => ExitApplication();

            _island.Show();
            AppLogger.Info("Application started.");

            if (HasArgument(e.Args, "--settings"))
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(OpenSettings));
            }
            else if (HasArgument(e.Args, "--demo"))
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => _island?.ExpandForDemo()));
            }
            else if (firstRun)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => _island?.ShowNotification(
                        "欢迎使用灵动岛",
                        "悬停展开，点击固定；右键或托盘图标可以打开菜单。",
                        TimeSpan.FromSeconds(5))));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Application startup failed.", ex);
            MessageBox.Show(
                $"灵动岛启动失败：\n{ex.Message}\n\n日志：{AppLogger.LogPath}",
                "灵动岛",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_instanceMutex is not null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex was already released.
                }
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings);
        _settingsWindow = window;
        _island?.SetTopmostSuppressed(true);
        window.SettingsSaved += OnSettingsSaved;
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _island?.SetTopmostSuppressed(false);
        };
        window.Show();
        window.Activate();
    }

    private void OnAgentSettingsChanged(object? sender, IslandSettings settings)
    {
        _settings = settings.Clone();
        if (_settingsStore?.TrySave(_settings, out var saveError) == false)
        {
            AppLogger.Error($"Unable to persist Agent model selection: {saveError}");
        }
    }

    private void OnSettingsSaved(IslandSettings settings)
    {
        _settings = settings.Clone();
        _island?.ApplySettings(_settings);
        if (_trayIcon is not null)
        {
            _trayIcon.StartWithWindows = _settings.StartWithWindows;
        }

        if (_settingsStore?.TrySave(_settings, out var saveError) == false)
        {
            MessageBox.Show(
                $"设置已应用，但写入配置文件失败：\n{saveError}",
                "灵动岛",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_startupService?.TrySetEnabled(_settings.StartWithWindows, out var startupError) == false
            && startupError is not null)
        {
            MessageBox.Show(
                $"开机启动设置失败：\n{startupError}",
                "灵动岛",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnTrayStartWithWindowsChanged(bool enabled)
    {
        _settings.StartWithWindows = enabled;

        if (_startupService?.TrySetEnabled(enabled, out var error) == false && error is not null)
        {
            MessageBox.Show(
                $"开机启动设置失败：\n{error}",
                "灵动岛",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _settings.StartWithWindows = _startupService.IsEnabled();
        }

        _settingsStore?.TrySave(_settings, out _);
        if (_trayIcon is not null)
        {
            _trayIcon.StartWithWindows = _settings.StartWithWindows;
        }
    }

    private void ExitApplication()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _island?.PrepareForShutdown();
        Shutdown();
    }

    private static bool HasArgument(string[] args, string value)
        => args.Any(argument => string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            AppLogger.Error("Unhandled dispatcher exception.", eventArgs.Exception);
            MessageBox.Show(
                $"灵动岛遇到未处理的错误：\n{eventArgs.Exception.Message}\n\n日志：{AppLogger.LogPath}",
                "灵动岛",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            eventArgs.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppLogger.Error("Unhandled AppDomain exception.", eventArgs.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }
}
