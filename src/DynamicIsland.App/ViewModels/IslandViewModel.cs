using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using DynamicIsland.App.Interop;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Models;
using Screen = System.Windows.Forms.Screen;

namespace DynamicIsland.App.ViewModels;

/// <summary>
/// Presentation state for the island. Kept free of window/native-window concerns so that
/// widgets can be added without touching the animation plumbing.
/// </summary>
public sealed class IslandViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _powerTimer;
    private readonly OutlookCalendarService _calendarService = new();
    private IslandSettings _settings;

    private DateTime _now = DateTime.Now;
    private bool _hasBattery;
    private bool _isCharging;
    private int _batteryPercent = 100;
    private string _powerLabel = "已接通电源";
    private string _screenText = "—";
    private string _uptimeText = "00:00";
    private bool _isExpanded;
    private bool _isPinned;
    private bool _isNotification;
    private string _notificationTitle = string.Empty;
    private string _notificationMessage = string.Empty;
    private FunctionTab _activeFunction = FunctionTab.FileShelf;
    private bool _calendarLoading;
    private bool _calendarLoaded;
    private bool _calendarError;
    private string _calendarStatusText = "今日";
    private string _calendarErrorTitle = string.Empty;
    private string _calendarErrorMessage = string.Empty;
    private DateTime _calendarLoadedAt;
    private int _calendarRequestId;

    public IslandViewModel(IslandSettings settings)
    {
        _settings = settings.Clone();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;

        _powerTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _powerTimer.Tick += OnPowerTick;

        RefreshClock();
        RefreshPower();
        RefreshScreen();
    }

    public string TimeText => _now.ToString("HH:mm");

    public string SecondsText => _now.ToString("ss");

    public string SecondsDisplay => ":" + SecondsText;

    public string ExpandedSeconds => _settings.ShowSeconds ? ":" + SecondsText : string.Empty;

    public string DateText => $"{_now:M月d日} {WeekdayName(_now.DayOfWeek)}";

    public string Greeting => _now.Hour switch
    {
        >= 5 and < 12 => "上午好",
        >= 12 and < 14 => "中午好",
        >= 14 and < 18 => "下午好",
        >= 18 and < 23 => "晚上好",
        _ => "夜深了"
    };

    /// <summary>The Windows account name displayed on the compact island.</summary>
    public string UserName { get; } = GetWindowsUserName();

    public const int MaxStagedFiles = 12;

    /// <summary>
    /// Session-scoped file shelf. Items are references to the original paths, so
    /// dragging them out copies the files instead of moving or deleting them.
    /// </summary>
    public ObservableCollection<StagedFileViewModel> StagedFiles { get; } = new();

    public bool HasStagedFiles => StagedFiles.Count > 0;

    public string ShelfCountText => $"{StagedFiles.Count} 个文件";

    public FunctionTab ActiveFunction => _activeFunction;

    public bool IsFileShelfActive => _activeFunction == FunctionTab.FileShelf;

    public bool IsCalendarActive => _activeFunction == FunctionTab.Calendar;

    public string ActiveFunctionTitle => IsFileShelfActive ? "文件暂存" : "Outlook 日历";

    public string FooterHint => IsFileShelfActive
        ? "拖入暂存 · 拖出复制 · 原文件保留"
        : "今日日程 · 点击圆点切换功能区";

    public ObservableCollection<CalendarEventItem> CalendarEvents { get; } = new();

    public bool IsCalendarLoading => _calendarLoading;

    public bool HasCalendarError => _calendarError;

    public bool HasCalendarEvents => CalendarEvents.Count > 0;

    public bool IsCalendarEmpty => _calendarLoaded && !_calendarLoading && !_calendarError
                                   && CalendarEvents.Count == 0;

    public string CalendarStatusText
    {
        get => _calendarStatusText;
        private set => SetProperty(ref _calendarStatusText, value);
    }

    public string CalendarErrorTitle
    {
        get => _calendarErrorTitle;
        private set => SetProperty(ref _calendarErrorTitle, value);
    }

    public string CalendarErrorMessage
    {
        get => _calendarErrorMessage;
        private set => SetProperty(ref _calendarErrorMessage, value);
    }

    public bool ShowSeconds => _settings.ShowSeconds;

    public bool AlwaysOnTop => _settings.AlwaysOnTop;

    public string TopmostLabel => _settings.AlwaysOnTop ? "始终置顶" : "普通层级";

    public string BatteryText => _hasBattery ? $"{_batteryPercent}%" : "交流电";

    public string PowerLabel
    {
        get => _powerLabel;
        private set => SetProperty(ref _powerLabel, value);
    }

    public double BatteryFillWidth => _hasBattery
        ? Math.Max(2.0, 23.0 * _batteryPercent / 100.0)
        : 23.0;

    public bool IsCharging
    {
        get => _isCharging;
        private set => SetProperty(ref _isCharging, value);
    }

    public string ScreenText
    {
        get => _screenText;
        private set => SetProperty(ref _screenText, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => SetProperty(ref _uptimeText, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinHint));
            }
        }
    }

    public bool IsNotification
    {
        get => _isNotification;
        set => SetProperty(ref _isNotification, value);
    }

    public string PinHint => IsPinned ? "已固定 · 点击取消" : "点击固定";

    public string NotificationTitle
    {
        get => _notificationTitle;
        set => SetProperty(ref _notificationTitle, value);
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        set => SetProperty(ref _notificationMessage, value);
    }

    public void Start()
    {
        RefreshClock();
        RefreshPower();
        RefreshScreen();
        _clockTimer.Start();
        _powerTimer.Start();
    }

    public void Stop()
    {
        _clockTimer.Stop();
        _powerTimer.Stop();
    }

    /// <summary>
    /// Adds existing files or folders to the shelf, ignoring duplicates, missing paths
    /// and anything beyond <see cref="MaxStagedFiles"/>.
    /// </summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            if (StagedFiles.Count >= MaxStagedFiles)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var isDirectory = Directory.Exists(fullPath);
            if (!isDirectory && !File.Exists(fullPath))
            {
                continue;
            }

            if (StagedFiles.Any(item => string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            StagedFiles.Add(new StagedFileViewModel(fullPath, isDirectory));
            added++;
        }

        if (added > 0)
        {
            RaiseShelfChanged();
        }

        return added;
    }

    public bool RemoveFile(StagedFileViewModel file)
    {
        if (!StagedFiles.Remove(file))
        {
            return false;
        }

        RaiseShelfChanged();
        return true;
    }

    public void ClearFiles()
    {
        if (StagedFiles.Count == 0)
        {
            return;
        }

        StagedFiles.Clear();
        RaiseShelfChanged();
    }

    private void RaiseShelfChanged()
    {
        OnPropertyChanged(nameof(HasStagedFiles));
        OnPropertyChanged(nameof(ShelfCountText));
    }

    public void SelectFunction(FunctionTab function)
    {
        if (_activeFunction != function)
        {
            _activeFunction = function;
            OnPropertyChanged(nameof(ActiveFunction));
            OnPropertyChanged(nameof(IsFileShelfActive));
            OnPropertyChanged(nameof(IsCalendarActive));
            OnPropertyChanged(nameof(ActiveFunctionTitle));
            OnPropertyChanged(nameof(FooterHint));
        }

        if (function == FunctionTab.Calendar)
        {
            _ = EnsureCalendarLoadedAsync();
        }
    }

    public Task EnsureCalendarLoadedAsync()
    {
        if (_calendarLoading)
        {
            return Task.CompletedTask;
        }

        if (_calendarLoaded && DateTime.Now - _calendarLoadedAt < TimeSpan.FromMinutes(2))
        {
            return Task.CompletedTask;
        }

        return RefreshCalendarAsync(force: false);
    }

    public async Task RefreshCalendarAsync(bool force = true)
    {
        if (_calendarLoading)
        {
            return;
        }

        if (!force && _calendarLoaded
            && DateTime.Now - _calendarLoadedAt < TimeSpan.FromMinutes(2))
        {
            return;
        }

        var request = ++_calendarRequestId;
        _calendarLoading = true;
        _calendarLoaded = false;
        _calendarError = false;
        CalendarEvents.Clear();
        CalendarErrorTitle = string.Empty;
        CalendarErrorMessage = string.Empty;
        CalendarStatusText = "正在读取…";

        OnPropertyChanged(nameof(IsCalendarLoading));
        OnPropertyChanged(nameof(HasCalendarError));
        OnPropertyChanged(nameof(HasCalendarEvents));
        OnPropertyChanged(nameof(IsCalendarEmpty));

        try
        {
            var loadTask = _calendarService.LoadTodayAsync(_settings.WebCalendarUrl);
            var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(15)));

            if (request != _calendarRequestId)
            {
                return;
            }

            if (!ReferenceEquals(completed, loadTask))
            {
                SetCalendarError("Outlook 未响应", "读取超时，请稍后点击刷新重试。");
                return;
            }

            var result = await loadTask;
            if (request != _calendarRequestId)
            {
                return;
            }

            if (result.HasError)
            {
                SetCalendarError(
                    result.ErrorTitle ?? "无法读取 Outlook 日历",
                    result.ErrorMessage ?? "请确认 Outlook 已安装并登录。");
                return;
            }

            foreach (var calendarEvent in result.Events)
            {
                CalendarEvents.Add(calendarEvent);
            }

            _calendarLoaded = true;
            _calendarLoadedAt = DateTime.Now;
            CalendarStatusText = CalendarEvents.Count == 0
                ? "今天没有日程"
                : $"{CalendarEvents.Count} 个日程";

            OnPropertyChanged(nameof(HasCalendarEvents));
            OnPropertyChanged(nameof(IsCalendarEmpty));
        }
        catch (Exception ex)
        {
            SetCalendarError("无法读取 Outlook 日历", ex.Message);
        }
        finally
        {
            if (request == _calendarRequestId)
            {
                _calendarLoading = false;
                OnPropertyChanged(nameof(IsCalendarLoading));
                OnPropertyChanged(nameof(IsCalendarEmpty));
            }
        }
    }

    private void SetCalendarError(string title, string message)
    {
        _calendarLoaded = true;
        _calendarLoadedAt = DateTime.Now;
        _calendarError = true;
        CalendarErrorTitle = title;
        CalendarErrorMessage = message;
        CalendarStatusText = "未连接";

        OnPropertyChanged(nameof(HasCalendarError));
        OnPropertyChanged(nameof(IsCalendarEmpty));
    }

    public void ApplySettings(IslandSettings settings)
    {
        var previousCalendarUrl = _settings.WebCalendarUrl;
        _settings = settings.Clone();
        OnPropertyChanged(nameof(ShowSeconds));
        OnPropertyChanged(nameof(AlwaysOnTop));
        OnPropertyChanged(nameof(TopmostLabel));
        OnPropertyChanged(nameof(SecondsDisplay));
        OnPropertyChanged(nameof(ExpandedSeconds));

        if (!string.Equals(previousCalendarUrl, _settings.WebCalendarUrl, StringComparison.Ordinal))
        {
            _calendarRequestId++;
            _calendarLoading = false;
            _calendarLoaded = false;
            _calendarError = false;
            CalendarEvents.Clear();
            CalendarErrorTitle = string.Empty;
            CalendarErrorMessage = string.Empty;
            CalendarStatusText = "今日";

            OnPropertyChanged(nameof(IsCalendarLoading));
            OnPropertyChanged(nameof(HasCalendarError));
            OnPropertyChanged(nameof(HasCalendarEvents));
            OnPropertyChanged(nameof(IsCalendarEmpty));

            if (IsCalendarActive)
            {
                _ = RefreshCalendarAsync(force: true);
            }
        }
    }

    private void RefreshClock()
    {
        _now = DateTime.Now;
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(SecondsText));
        OnPropertyChanged(nameof(SecondsDisplay));
        OnPropertyChanged(nameof(ExpandedSeconds));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(Greeting));

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours}小时 {uptime.Minutes:00}分"
            : $"{uptime.Minutes}分 {uptime.Seconds:00}秒";
    }

    private void RefreshPower()
    {
        try
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status))
            {
                return;
            }

            _hasBattery = status.BatteryFlag != 128 && status.BatteryLifePercent != 255;
            _batteryPercent = _hasBattery
                ? Math.Clamp((int)status.BatteryLifePercent, 0, 100)
                : 100;
            IsCharging = _hasBattery && status.AcLineStatus == 1;
            PowerLabel = !_hasBattery
                ? "已接通电源"
                : IsCharging
                    ? "充电中"
                    : "电池供电";

            OnPropertyChanged(nameof(BatteryText));
            OnPropertyChanged(nameof(BatteryFillWidth));
            OnPropertyChanged(nameof(IsCharging));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to read power status.", ex);
        }
    }

    private void RefreshScreen()
    {
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds;
            ScreenText = bounds is null ? "—" : $"{bounds.Value.Width} × {bounds.Value.Height}";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to read the primary screen size.", ex);
            ScreenText = "—";
        }
    }

    private static string GetWindowsUserName()
    {
        try
        {
            var userName = Environment.UserName?.Trim();
            return string.IsNullOrWhiteSpace(userName) ? "用户" : userName;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to read the Windows user name.", ex);
            return "用户";
        }
    }

    private static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    private void OnClockTick(object? sender, EventArgs e) => RefreshClock();

    private void OnPowerTick(object? sender, EventArgs e) => RefreshPower();

    public void Dispose()
    {
        Stop();
        _clockTimer.Tick -= OnClockTick;
        _powerTimer.Tick -= OnPowerTick;
    }
}
