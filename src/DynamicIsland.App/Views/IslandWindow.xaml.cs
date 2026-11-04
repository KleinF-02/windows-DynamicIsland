using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.App.Interop;
using DynamicIsland.App.ViewModels;
using DynamicIsland.Core.Layout;
using DynamicIsland.Core.Models;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace DynamicIsland.App.Views;

public partial class IslandWindow : Window
{
    private readonly IslandViewModel _viewModel;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _notificationTimer;

    private IslandSettings _settings;
    private IslandVisualState _state = IslandVisualState.Compact;
    private FrameworkElement _activeView;
    private HwndSource? _hwndSource;
    private bool _loaded;
    private bool _hovering;
    private bool _pinned;
    private bool _menuOpen;
    private bool _allowClose;
    private bool _topmostSuppressed;

    public IslandWindow(IslandSettings settings)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _viewModel = new IslandViewModel(_settings);
        DataContext = _viewModel;
        _activeView = CompactView;

        Topmost = _settings.AlwaysOnTop;
        Opacity = _settings.Opacity;

        _collapseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;

        _notificationTimer = new DispatcherTimer(DispatcherPriority.Background);
        _notificationTimer.Tick += NotificationTimer_Tick;
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void ApplySettings(IslandSettings settings)
    {
        _settings = settings.Clone();
        _viewModel.ApplySettings(_settings);
        Topmost = !_topmostSuppressed && _settings.AlwaysOnTop;
        Opacity = _settings.Opacity;
        PositionWindow(animate: false);
    }

    /// <summary>
    /// Temporarily drops the topmost flag while a normal settings window is open, so the
    /// island does not cover that window's header.
    /// </summary>
    public void SetTopmostSuppressed(bool suppressed)
    {
        _topmostSuppressed = suppressed;
        Topmost = !_topmostSuppressed && _settings.AlwaysOnTop;
    }

    public void ShowNotification(string title, string message, TimeSpan? duration = null)
    {
        if (!_loaded)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ShowNotification(title, message, duration)));
            return;
        }

        _notificationTimer.Stop();
        _viewModel.NotificationTitle = title;
        _viewModel.NotificationMessage = message;
        _pinned = false;
        _viewModel.IsPinned = false;

        SetState(IslandVisualState.Notification, animate: true);

        _notificationTimer.Interval = duration ?? TimeSpan.FromSeconds(4.5);
        _notificationTimer.Start();
    }

    public void ExpandForDemo()
    {
        if (!_loaded)
        {
            return;
        }

        _pinned = true;
        _viewModel.IsPinned = true;
        SetState(IslandVisualState.Expanded, animate: true);
    }

    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        _viewModel.Start();
        Topmost = _settings.AlwaysOnTop;
        PositionWindow(animate: false);
        ShowNotification("灵动岛", "我在这里。", TimeSpan.FromSeconds(2.5));
    }

    public void HideToTray()
    {
        _notificationTimer.Stop();
        _collapseTimer.Stop();
        _pinned = false;
        _viewModel.IsPinned = false;
        SetState(IslandVisualState.Compact, animate: false);
        _viewModel.Stop();
        Hide();
    }

    public void PrepareForShutdown()
    {
        _allowClose = true;
        _viewModel.Stop();
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WindowProc);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _viewModel.Start();
        PositionWindow(animate: false);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        _notificationTimer.Stop();
        _viewModel.Dispose();
        _hwndSource?.RemoveHook(WindowProc);
        _hwndSource = null;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        _collapseTimer.Stop();

        if (_state == IslandVisualState.Compact)
        {
            SetState(IslandVisualState.Expanded, animate: true);
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;

        if (_state == IslandVisualState.Expanded && !_pinned)
        {
            ScheduleCollapse();
        }
    }

    private void Shell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_state == IslandVisualState.Notification)
        {
            DismissNotification();
            e.Handled = true;
            return;
        }

        // The first click pins the island open; the next click releases it.
        _pinned = !_pinned;
        _viewModel.IsPinned = _pinned;

        if (_state == IslandVisualState.Compact)
        {
            _pinned = true;
            _viewModel.IsPinned = true;
            SetState(IslandVisualState.Expanded, animate: true);
        }
        else if (!_pinned && !_hovering)
        {
            ScheduleCollapse();
        }

        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void DemoNotification_Click(object sender, RoutedEventArgs e)
        => ShowNotification(
            "通知通道已接通",
            "后续可以在这里接入媒体播放、日程和充电提醒。",
            TimeSpan.FromSeconds(6));

    private void Hide_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Exit_Click(object sender, RoutedEventArgs e)
        => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void IslandMenu_Opened(object sender, RoutedEventArgs e)
    {
        _menuOpen = true;
        _collapseTimer.Stop();
    }

    private void IslandMenu_Closed(object sender, RoutedEventArgs e)
    {
        _menuOpen = false;

        if (!_hovering && !_pinned && _state == IslandVisualState.Expanded)
        {
            ScheduleCollapse();
        }
    }

    private void ScheduleCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();

        if (_hovering || _pinned || _menuOpen || _state != IslandVisualState.Expanded)
        {
            return;
        }

        SetState(IslandVisualState.Compact, animate: true);
    }

    private void NotificationTimer_Tick(object? sender, EventArgs e)
    {
        _notificationTimer.Stop();
        DismissNotification();
    }

    private void DismissNotification()
    {
        _notificationTimer.Stop();

        if (_state != IslandVisualState.Notification)
        {
            return;
        }

        SetState(_hovering ? IslandVisualState.Expanded : IslandVisualState.Compact, animate: true);
    }

    private void SetState(IslandVisualState state, bool animate)
    {
        var changed = _state != state;
        _state = state;

        _viewModel.IsExpanded = state == IslandVisualState.Expanded;
        _viewModel.IsNotification = state == IslandVisualState.Notification;
        _viewModel.IsPinned = _pinned && state == IslandVisualState.Expanded;

        var shouldAnimate = animate && _loaded && _settings.AnimationsEnabled;
        PositionWindow(shouldAnimate);

        if (changed || _activeView != GetView(state))
        {
            SwitchContent(state, shouldAnimate);
        }
    }

    private void PositionWindow(bool animate)
    {
        var screen = ResolveScreen();
        var outer = IslandMetrics.Outer(_state);
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        var placement = IslandPlacement.TopCenter(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            outer.Width,
            _settings.TopMargin);

        AnimateBounds(outer, placement.Left, placement.Top, animate);
    }

    private void AnimateBounds(IslandSize target, double targetLeft, double targetTop, bool animate)
    {
        var width = Math.Max(1.0, Math.Round(target.Width));
        var height = Math.Max(1.0, Math.Round(target.Height));
        var left = Math.Round(targetLeft);
        var top = Math.Round(targetTop);

        if (!animate || !_loaded)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            return;
        }

        var fromWidth = ActualWidth > 0 ? ActualWidth : width;
        var fromHeight = ActualHeight > 0 ? ActualHeight : height;
        var fromLeft = double.IsFinite(Left) ? Left : left;

        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);

        Width = width;
        Height = height;
        Left = left;
        Top = top;

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(300);

        BeginAnimation(
            WidthProperty,
            new DoubleAnimation(fromWidth, width, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            });

        BeginAnimation(
            HeightProperty,
            new DoubleAnimation(fromHeight, height, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            });

        BeginAnimation(
            LeftProperty,
            new DoubleAnimation(fromLeft, left, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            });
    }

    private void SwitchContent(IslandVisualState state, bool animate)
    {
        var target = GetView(state);
        if (ReferenceEquals(target, _activeView) && target.Visibility == Visibility.Visible)
        {
            return;
        }

        var previous = _activeView;
        _activeView = target;

        target.BeginAnimation(OpacityProperty, null);
        target.Visibility = Visibility.Visible;

        var transform = target.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            target.RenderTransform = transform;
        }

        if (animate)
        {
            target.Opacity = 0;
            target.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(190))
                {
                    BeginTime = TimeSpan.FromMilliseconds(40),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            transform.Y = 6;
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            target.Opacity = 1;
            transform.Y = 0;
        }

        foreach (FrameworkElement view in ContentViews())
        {
            if (ReferenceEquals(view, target) || view.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (!animate)
            {
                view.BeginAnimation(OpacityProperty, null);
                view.Opacity = 0;
                view.Visibility = Visibility.Collapsed;
                continue;
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fade.Completed += (_, _) =>
            {
                if (!ReferenceEquals(_activeView, view))
                {
                    view.BeginAnimation(OpacityProperty, null);
                    view.Opacity = 0;
                    view.Visibility = Visibility.Collapsed;
                }
            };

            view.BeginAnimation(OpacityProperty, fade);
        }
    }

    private FrameworkElement GetView(IslandVisualState state) => state switch
    {
        IslandVisualState.Compact => CompactView,
        IslandVisualState.Expanded => ExpandedView,
        IslandVisualState.Notification => NotificationView,
        _ => CompactView
    };

    private IEnumerable<FrameworkElement> ContentViews()
    {
        yield return CompactView;
        yield return ExpandedView;
        yield return NotificationView;
    }

    private Screen ResolveScreen()
    {
        var screens = Screen.AllScreens;

        if (!string.IsNullOrWhiteSpace(_settings.PreferredScreen))
        {
            var preferred = screens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, _settings.PreferredScreen, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return Screen.PrimaryScreen ?? screens.First();
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmNcHitTest || !_loaded)
        {
            return IntPtr.Zero;
        }

        // The window contains 14 DIPs of transparent padding for the shadow. Clicks in
        // that padding should reach the window underneath instead of being swallowed.
        if (NativeMethods.GetCursorPos(out var cursor) && NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var paddingX = (int)Math.Round(IslandMetrics.ShadowPadding * dpi.DpiScaleX);
            var paddingY = (int)Math.Round(IslandMetrics.ShadowPadding * dpi.DpiScaleY);

            var inside = cursor.X >= windowRect.Left + paddingX
                         && cursor.X <= windowRect.Right - paddingX
                         && cursor.Y >= windowRect.Top + paddingY
                         && cursor.Y <= windowRect.Bottom - paddingY;

            if (!inside)
            {
                handled = true;
                return new IntPtr(NativeMethods.HtTransparent);
            }
        }

        return IntPtr.Zero;
    }
}
