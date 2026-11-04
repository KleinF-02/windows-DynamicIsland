using System.ComponentModel;
using System.IO;
using System.Text;
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
using Button = System.Windows.Controls.Button;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
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
    private bool _dragActive;
    private bool _dragOutActive;
    private Point _fileDragStart;
    private StagedFileViewModel? _fileDragCandidate;
    private DateTime _suppressClickUntil;

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

        // A drag/drop ends with a mouse-up; don't treat it as a pin toggle.
        if (DateTime.UtcNow < _suppressClickUntil)
        {
            e.Handled = true;
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

    private void Shell_DragEnter(object sender, DragEventArgs e)
    {
        if (!HasFilePayload(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        _viewModel.SelectFunction(FunctionTab.FileShelf);
        _dragActive = true;
        _collapseTimer.Stop();
        SetShelfHighlight(true);

        if (_state != IslandVisualState.Expanded)
        {
            _notificationTimer.Stop();
            SetState(IslandVisualState.Expanded, animate: true);
        }

        e.Handled = true;
    }

    private void Shell_DragOver(object sender, DragEventArgs e)
    {
        var acceptsFiles = HasFilePayload(e.Data);
        e.Effects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!acceptsFiles)
        {
            return;
        }

        _dragActive = true;
        _collapseTimer.Stop();
        SetShelfHighlight(true);

        if (_state != IslandVisualState.Expanded)
        {
            _notificationTimer.Stop();
            SetState(IslandVisualState.Expanded, animate: true);
        }
    }

    private void Shell_DragLeave(object sender, DragEventArgs e)
    {
        _dragActive = false;
        SetShelfHighlight(false);

        if (!_hovering && !_pinned)
        {
            ScheduleCollapse();
        }

        e.Handled = true;
    }

    private void Shell_Drop(object sender, DragEventArgs e)
    {
        _dragActive = false;
        _suppressClickUntil = DateTime.UtcNow.AddMilliseconds(300);
        SetShelfHighlight(false);
        e.Effects = DragDropEffects.Copy;

        var paths = ExtractDroppedPaths(e.Data);
        if (paths.Count > 0)
        {
            _viewModel.SelectFunction(FunctionTab.FileShelf);
            _viewModel.AddFiles(paths);
            _pinned = true;
            _viewModel.IsPinned = true;

            if (_state != IslandVisualState.Expanded)
            {
                SetState(IslandVisualState.Expanded, animate: true);
            }
        }

        e.Handled = true;
    }

    private void FileTab_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectFunction(FunctionTab.FileShelf);
        e.Handled = true;
    }

    private void CalendarTab_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectFunction(FunctionTab.Calendar);
        e.Handled = true;
    }

    private void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.RefreshCalendarAsync(force: true);
        e.Handled = true;
    }

    private void OpenSettingsFromCalendar_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearFiles();
        e.Handled = true;
    }

    private void RemoveStagedFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StagedFileViewModel file })
        {
            _viewModel.RemoveFile(file);
        }

        e.Handled = true;
    }

    private void StagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _fileDragStart = e.GetPosition(this);
        _fileDragCandidate = (sender as FrameworkElement)?.DataContext as StagedFileViewModel;
    }

    private void StagedFile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _fileDragCandidate = null;
        e.Handled = true;
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_fileDragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var file = _fileDragCandidate;
        _fileDragCandidate = null;

        if (!file.Exists)
        {
            _viewModel.RemoveFile(file);
            ShowNotification("文件已不在原位置", "已从暂存区移除。", TimeSpan.FromSeconds(3));
            return;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { file.FullPath });

        _dragOutActive = true;
        _suppressClickUntil = DateTime.UtcNow.AddMilliseconds(300);

        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragOutActive = false;

            if (!_hovering && !_pinned)
            {
                ScheduleCollapse();
            }
        }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragCandidate = null;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _fileDragCandidate = null;
    }

    private void SetShelfHighlight(bool active)
    {
        ShelfHighlight.Opacity = active ? 1 : 0;
    }

    private static bool HasFilePayload(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
           || data.GetDataPresent("FileNameW")
           || data.GetDataPresent("FileName");

    private static IReadOnlyList<string> ExtractDroppedPaths(IDataObject data)
    {
        // FileDrop is the normal OLE format. FileNameW/FileName are fallbacks used
        // by a few drag sources (and by the PowerShell drag helper used in testing).
        foreach (var format in new[] { DataFormats.FileDrop, "FileNameW", "FileName" })
        {
            var paths = ParseDropPayload(data.GetData(format));
            if (paths.Count > 0)
            {
                return paths;
            }
        }

        return [];
    }

    private static IReadOnlyList<string> ParseDropPayload(object? payload)
    {
        switch (payload)
        {
            case string[] files:
                return files;
            case string file:
                return [file];
            case MemoryStream stream:
                return ParseFileList(stream.ToArray());
            case byte[] bytes:
                return ParseFileList(bytes);
            case System.Collections.IEnumerable entries:
                var paths = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry is string path)
                    {
                        paths.Add(path);
                    }
                }

                return paths;
            default:
                return [];
        }
    }

    /// <summary>
    /// Parses the DROPFILES structure that some OLE drag sources expose as a
    /// MemoryStream (pFiles offset + fWide flag), or a plain double-null list.
    /// </summary>
    private static IReadOnlyList<string> ParseFileList(byte[] bytes)
    {
        if (bytes.Length < 2)
        {
            return [];
        }

        var offset = 0;
        var wide = true;

        if (bytes.Length >= 20)
        {
            var declaredOffset = BitConverter.ToInt32(bytes, 0);
            if (declaredOffset > 0 && declaredOffset < bytes.Length)
            {
                offset = declaredOffset;
                wide = BitConverter.ToInt32(bytes, 16) != 0;
            }
        }

        var text = wide
            ? Encoding.Unicode.GetString(bytes, offset, bytes.Length - offset)
            : Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);

        return text.Split('\0')
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .ToArray();
    }

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

        if (_hovering || _pinned || _menuOpen || _dragActive || _dragOutActive
            || _state != IslandVisualState.Expanded)
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
            topMargin: 0);

        // TopMargin is measured to the visible capsule, not to the transparent
        // window. Pull the window upward by the shadow padding so 0 means flush.
        var windowTop = placement.Top + _settings.TopMargin - IslandMetrics.ShadowPadding;

        AnimateBounds(outer, placement.Left, windowTop, animate);
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

        var (scale, transform) = GetContentTransforms(target);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

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

            // Zoom out of the top edge: the island grows from the screen edge
            // instead of merely re-laying out at the new size.
            var scaleFrom = ReferenceEquals(target, ExpandedView) ? 0.92 : 0.95;
            var scaleEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var scaleDuration = TimeSpan.FromMilliseconds(300);

            scale.ScaleX = 1;
            scale.ScaleY = 1;
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(scaleFrom, 1, scaleDuration) { EasingFunction = scaleEase });
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(scaleFrom, 1, scaleDuration) { EasingFunction = scaleEase });

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
            scale.ScaleX = 1;
            scale.ScaleY = 1;
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

    private (ScaleTransform Scale, TranslateTransform Translate) GetContentTransforms(FrameworkElement view)
    {
        if (ReferenceEquals(view, CompactView))
        {
            return (CompactScale, CompactTranslate);
        }

        if (ReferenceEquals(view, ExpandedView))
        {
            return (ExpandedScale, ExpandedTranslate);
        }

        return (NotificationScale, NotificationTranslate);
    }

    private IEnumerable<FrameworkElement> ContentViews()
    {
        yield return CompactView;
        yield return ExpandedView;
        yield return NotificationView;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
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
