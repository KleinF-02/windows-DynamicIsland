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

    public void ApplySettings(IslandSettings settings)
    {
        _settings = settings.Clone();
        OnPropertyChanged(nameof(ShowSeconds));
        OnPropertyChanged(nameof(AlwaysOnTop));
        OnPropertyChanged(nameof(TopmostLabel));
        OnPropertyChanged(nameof(SecondsDisplay));
        OnPropertyChanged(nameof(ExpandedSeconds));
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
