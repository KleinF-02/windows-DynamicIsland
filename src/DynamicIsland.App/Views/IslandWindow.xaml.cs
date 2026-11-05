using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.App.Animations;
using DynamicIsland.App.Interop;
using DynamicIsland.App.Services;
using DynamicIsland.App.ViewModels;
using DynamicIsland.Core.Layout;
using DynamicIsland.Core.Models;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Canvas = System.Windows.Controls.Canvas;
using CheckBox = System.Windows.Controls.CheckBox;
using Ellipse = System.Windows.Shapes.Ellipse;
using Grid = System.Windows.Controls.Grid;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
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
    private readonly DispatcherTimer _heightSyncTimer;
    private readonly DispatcherTimer _typingHoldTimer;
    private readonly DispatcherTimer _startMenuGuardTimer;
    private int _boundsAnimationId;
    private DateTime _nativeHeightStart;
    private BlurEffect? _transitionBlur;
    private int _transitionBlurId;
    private double _cornerRadiusFrom;
    private double _cornerRadiusTo;
    private RectangleGeometry? _contentMaskGeometry;
    private bool _contentMaskActive;
    private bool _contentMaskExpanding;
    private double _nativeWidthFrom;
    private double _nativeWidthTo;
    private double _nativeHeightFrom;
    private double _nativeHeightTo;
    private TimeSpan _nativeHeightDuration;
    private EasingFunctionBase? _nativeHeightEase;
    private readonly DispatcherTimer _notificationTimer;

    private IslandSettings _settings;
    private IslandVisualState _state = IslandVisualState.Compact;
    private FrameworkElement _activeView;
    private HwndSource? _hwndSource;
    private bool _loaded;
    private bool _hovering;
    private bool _pinned;
    private bool _editingTodo;
    private bool _editingTranslation;
    private bool _editingAgent;
    private DateTime _translationTypingHoldUntil;
    private DateTime _agentTypingHoldUntil;
    private bool _menuOpen;
    private bool _allowClose;
    private bool _topmostSuppressed;
    private bool _dragActive;
    private bool _dragOutActive;
    private bool _startMenuOpen;
    private DateTime _startMenuDragGraceUntil;
    private IslandVisualState _stateBeforeStartMenu = IslandVisualState.Compact;
    private readonly HashSet<uint> _shellProcessIds = new();
    private IntPtr _lastForegroundWindow;
    private Point _fileDragStart;
    private StagedFileViewModel? _fileDragCandidate;
    private readonly HashSet<string> _completingTodoIds = new(StringComparer.Ordinal);

    public IslandWindow(IslandSettings settings)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _viewModel = new IslandViewModel(_settings);
        DataContext = _viewModel;
        _activeView = CompactView;
        _viewModel.AgentMessages.CollectionChanged += AgentMessages_CollectionChanged;

        Topmost = _settings.AlwaysOnTop;
        Opacity = _settings.Opacity;

        _collapseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;

        _heightSyncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _heightSyncTimer.Tick += (_, _) => ApplyNativeHeight();

        _typingHoldTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _typingHoldTimer.Tick += TypingHoldTimer_Tick;

        _startMenuGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _startMenuGuardTimer.Tick += StartMenuGuard_Tick;

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
        _editingTodo = false;
        _editingTranslation = false;
        _editingAgent = false;
        _translationTypingHoldUntil = DateTime.MinValue;
        _agentTypingHoldUntil = DateTime.MinValue;
        _typingHoldTimer.Stop();
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

        _pinned = false;
        _viewModel.IsPinned = false;
        SetState(IslandVisualState.Expanded, animate: true);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // IME candidate windows can deactivate the WPF window while the Agent
        // TextBox still owns keyboard focus. Do not collapse during that case.
        _editingTodo = false;

        if (_state == IslandVisualState.Expanded && !_pinned && !_hovering
            && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive
            && !_menuOpen && !_dragActive && !_dragOutActive)
        {
            ScheduleCollapse();
        }
    }

    private void StartMenuGuard_Tick(object? sender, EventArgs e)
    {
        if (!_loaded || _hwndSource is null || !IsVisible
            || _topmostSuppressed || !_settings.AlwaysOnTop)
        {
            if (_startMenuOpen)
            {
                LeaveStartMenu();
            }

            return;
        }

        if (IsStartMenuForeground())
        {
            if (!_startMenuOpen)
            {
                _startMenuOpen = true;
                _stateBeforeStartMenu = _state;
                _collapseTimer.Stop();
            }

            _startMenuDragGraceUntil = DateTime.UtcNow.AddSeconds(3);

            // Keep the island as a small top drop target instead of covering the
            // Start menu. A drag started from Start can then enter it.
            if (!_dragActive && _state != IslandVisualState.Compact)
            {
                SetState(IslandVisualState.Compact, animate: true);
            }

            ReassertTopmost();
        }
        else if (_startMenuOpen)
        {
            LeaveStartMenu();
        }
    }

    private void LeaveStartMenu()
    {
        _startMenuOpen = false;

        if (_dragActive)
        {
            ReassertTopmost();
            return;
        }

        if (_stateBeforeStartMenu == IslandVisualState.Expanded
            && (_pinned || _hovering))
        {
            SetState(IslandVisualState.Expanded, animate: true);
        }
        else if (!_pinned && !_hovering && _state == IslandVisualState.Compact)
        {
            // Already compact; nothing else to do.
        }
        else if (!_pinned && !_hovering)
        {
            ScheduleCollapse();
        }
    }

    private bool IsStartMenuForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0)
        {
            return false;
        }

        if (foreground != _lastForegroundWindow)
        {
            _lastForegroundWindow = foreground;
            RefreshShellProcessIds();
        }

        return _shellProcessIds.Contains(processId);
    }

    private void RefreshShellProcessIds()
    {
        _shellProcessIds.Clear();

        foreach (var processName in new[] { "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                _shellProcessIds.Add((uint)process.Id);
                process.Dispose();
            }
        }
    }

    private void ReassertTopmost()
    {
        if (_hwndSource is null || _topmostSuppressed || !_settings.AlwaysOnTop)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwndSource.Handle,
            new IntPtr(-1),
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoMove | NativeMethods.SwpNoActivate);
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
        _editingTodo = false;
        _editingTranslation = false;
        _editingAgent = false;
        _translationTypingHoldUntil = DateTime.MinValue;
        _agentTypingHoldUntil = DateTime.MinValue;
        _typingHoldTimer.Stop();
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
        // Keep it out of Alt+Tab, but allow activation when the user clicks a todo
        // input. ShowActivated=False still prevents stealing focus on hover/show.
        extendedStyle |= NativeMethods.WsExToolWindow;
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
        _startMenuGuardTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        _heightSyncTimer.Stop();
        _startMenuGuardTimer.Stop();
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

        if (_state == IslandVisualState.Expanded && !_pinned && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive)
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

        // Pin/Unpin is intentionally only available from the dedicated footer
        // button. Clicking the compact pill merely expands it.
        if (_state == IslandVisualState.Compact)
        {
            SetState(IslandVisualState.Expanded, animate: true);
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state == IslandVisualState.Compact)
        {
            SetState(IslandVisualState.Expanded, animate: true);
        }
        else
        {
            _pinned = !_pinned;
            _viewModel.IsPinned = _pinned;

            if (_pinned)
            {
                _collapseTimer.Stop();
            }
            else if (!_hovering && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive)
            {
                ScheduleCollapse();
            }
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
        if (!AcceptsDropPayload(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        SelectFunction(FunctionTab.Shortcuts);
        _dragActive = true;
        _collapseTimer.Stop();

        if (_state != IslandVisualState.Expanded)
        {
            _notificationTimer.Stop();
            SetState(IslandVisualState.Expanded, animate: true);
        }

        e.Handled = true;
    }

    private void Shell_DragOver(object sender, DragEventArgs e)
    {
        var acceptsFiles = AcceptsDropPayload(e.Data);
        e.Effects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!acceptsFiles)
        {
            return;
        }

        _dragActive = true;
        _collapseTimer.Stop();

        if (_state != IslandVisualState.Expanded)
        {
            _notificationTimer.Stop();
            SetState(IslandVisualState.Expanded, animate: true);
        }
    }

    private void Shell_DragLeave(object sender, DragEventArgs e)
    {
        _dragActive = false;

        if (!_hovering && !_pinned && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive)
        {
            ScheduleCollapse();
        }

        e.Handled = true;
    }

    private void Shell_Drop(object sender, DragEventArgs e)
    {
        _dragActive = false;
        e.Effects = DragDropEffects.Copy;

        var paths = ExtractDroppedPaths(e.Data);
        if (paths.Count > 0)
        {
            SelectFunction(FunctionTab.Shortcuts);
            var pinned = _viewModel.PinShortcuts(paths);
            if (pinned == 0)
            {
                ShowNotification(
                    "没有可添加的快捷方式",
                    "请拖入 .lnk、.exe、.url 或 .appref-ms 文件。",
                    TimeSpan.FromSeconds(4));
            }

            if (_state != IslandVisualState.Expanded)
            {
                SetState(IslandVisualState.Expanded, animate: true);
            }
        }

        e.Handled = true;
    }

    private void ShortcutsTab_Click(object sender, RoutedEventArgs e)
    {
        SelectFunction(FunctionTab.Shortcuts);
        e.Handled = true;
    }

    private void ShortcutTile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement root
            || root.DataContext is not ShortcutSlotViewModel { HasShortcut: true })
        {
            return;
        }

        System.Windows.Controls.Panel.SetZIndex(root, 20);
        if (FindNamed<Button>(root, "ShortcutRemove") is { } remove)
        {
            remove.Visibility = Visibility.Visible;
        }

        var scale = EnsureMutableTransform<ScaleTransform>(root);
        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.16, duration) { EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.16, duration) { EasingFunction = ease });
    }

    private void ShortcutTile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement root
            || root.DataContext is not ShortcutSlotViewModel { HasShortcut: true })
        {
            return;
        }

        System.Windows.Controls.Panel.SetZIndex(root, 0);
        if (FindNamed<Button>(root, "ShortcutRemove") is { } remove)
        {
            remove.Visibility = Visibility.Collapsed;
        }

        var scale = EnsureMutableTransform<ScaleTransform>(root);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, duration) { EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, duration) { EasingFunction = ease });
    }

    private void ShortcutTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement root
            && root.DataContext is ShortcutSlotViewModel slot)
        {
            if (slot.HasShortcut)
            {
                LaunchShortcut(slot);
            }
            else
            {
                OpenShortcutPicker(slot);
            }

            e.Handled = true;
        }
    }

    private void OpenShortcutPicker(ShortcutSlotViewModel slot)
    {
        SetTopmostSuppressed(true);
        try
        {
            var dialog = new ShortcutPickerWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedPath is { } path)
            {
                var pinned = _viewModel.PinShortcuts([path], slot);
                if (pinned == 0)
                {
                    ShowNotification(
                        "无法添加这个快捷方式",
                        "请选择 .lnk、.exe、.url 或 .appref-ms 文件。",
                        TimeSpan.FromSeconds(4));
                }
            }
        }
        finally
        {
            SetTopmostSuppressed(false);
        }
    }

    private void RemoveShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutSlotViewModel slot })
        {
            _viewModel.RemoveShortcut(slot);

            if (FindAncestor<Grid>(sender as DependencyObject) is { } root)
            {
                ResetShortcutTile(root);
            }
        }

        e.Handled = true;
    }

    private void ResetShortcutTile(FrameworkElement root)
    {
        System.Windows.Controls.Panel.SetZIndex(root, 0);

        var scale = EnsureMutableTransform<ScaleTransform>(root);
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, duration) { EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, duration) { EasingFunction = ease });

        if (FindNamed<Button>(root, "ShortcutRemove") is { } remove)
        {
            remove.Visibility = Visibility.Collapsed;
        }
    }

    private void ShortcutTile_DragOver(object sender, DragEventArgs e)
    {
        _dragActive = true;
        _collapseTimer.Stop();
        e.Effects = AcceptsDropPayload(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ShortcutTile_DragLeave(object sender, DragEventArgs e)
    {
        _dragActive = false;

        if (!_hovering && !_pinned && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive)
        {
            ScheduleCollapse();
        }

        e.Handled = true;
    }

    private void ShortcutTile_Drop(object sender, DragEventArgs e)
    {
        _dragActive = false;
        var slot = (sender as FrameworkElement)?.DataContext as ShortcutSlotViewModel;
        var node = slot is null ? string.Empty : $"「{slot.DisplayName}」";
        var paths = ExtractDroppedPaths(e.Data);
        var pinned = _viewModel.PinShortcuts(paths, slot);

        if (pinned == 0)
        {
            ShowNotification(
                "没有可添加的快捷方式",
                $"请拖入 .lnk、.exe、.url 或 .appref-ms 文件{node}。",
                TimeSpan.FromSeconds(4));
        }

        e.Handled = true;
    }

    private void OpenShortcutMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.DataContext is ShortcutSlotViewModel { HasShortcut: true } slot)
        {
            LaunchShortcut(slot);
        }

        e.Handled = true;
    }

    private void RemoveShortcutMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.DataContext is ShortcutSlotViewModel slot)
        {
            _viewModel.RemoveShortcut(slot);
        }

        e.Handled = true;
    }

    private void LaunchShortcut(ShortcutSlotViewModel slot)
    {
        try
        {
            ShortcutLauncher.Launch(slot.Path!);
        }
        catch (Exception ex)
        {
            ShowNotification(
                "无法打开快捷方式",
                $"{slot.DisplayName}\n{ex.Message}",
                TimeSpan.FromSeconds(4));
        }
    }

    private static T EnsureMutableTransform<T>(FrameworkElement element)
        where T : Transform, new()
    {
        if (element.RenderTransform is T current && !current.IsFrozen)
        {
            return current;
        }

        var mutable = element.RenderTransform is T frozen
            ? (T)frozen.Clone()
            : new T();
        element.RenderTransform = mutable;
        return mutable;
    }

    private static T? FindNamed<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindNamed<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void SelectFunction(FunctionTab function)
    {
        // Translate and Agent share this policy: tab entry only changes size,
        // never pin state. Hovering the expanded window keeps it open; leaving
        // follows the normal collapse timer.
        var wasWorkspace = _viewModel.IsWorkspaceActive;
        var previousFunction = _viewModel.ActiveFunction;

        if (_viewModel.IsTranslateActive && function != FunctionTab.Translate)
        {
            _editingTranslation = false;
            _translationTypingHoldUntil = DateTime.MinValue;
        }

        if (_viewModel.IsAgentActive && function != FunctionTab.Agent)
        {
            _editingAgent = false;
            _agentTypingHoldUntil = DateTime.MinValue;
        }

        _viewModel.SelectFunction(function);

        if (_state == IslandVisualState.Expanded
            && wasWorkspace != _viewModel.IsWorkspaceActive)
        {
            PositionWindow(animate: true);
        }

        if (_state == IslandVisualState.Expanded
            && previousFunction != function
            && _settings.AnimationsEnabled)
        {
            AnimateFunctionContent();
            StartTransitionBlur(peakRadius: 4.0, durationMs: 240);
        }
    }

    private void AnimateFunctionContent()
    {
        FunctionContentHost.BeginAnimation(OpacityProperty, null);
        FunctionContentHost.Opacity = 1;

        var fade = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(fade, 60);
        FunctionContentHost.BeginAnimation(OpacityProperty, fade);
    }

    private void StartTransitionBlur(double peakRadius, int durationMs, int beginMs = 0)
    {
        // Rendering tier 2 is hardware accelerated; on software/remote sessions
        // prefer the existing fade/scale transition rather than dropping frames.
        if (!_settings.AnimationsEnabled || (RenderCapability.Tier >> 16) < 2)
        {
            return;
        }

        var id = ++_transitionBlurId;
        var blur = _transitionBlur ??= new BlurEffect
        {
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance
        };

        ContentRoot.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        blur.Radius = 0;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(
            peakRadius,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(45)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));

        animation.Completed += (_, _) =>
        {
            if (id != _transitionBlurId)
            {
                return;
            }

            blur.BeginAnimation(BlurEffect.RadiusProperty, null);
            blur.Radius = 0;
            ContentRoot.Effect = null;
            _transitionBlur = null;
        };

        Timeline.SetDesiredFrameRate(animation, 60);
        blur.BeginAnimation(BlurEffect.RadiusProperty, animation);
    }

    private void CalendarTab_Click(object sender, RoutedEventArgs e)
    {
        SelectFunction(FunctionTab.Calendar);
        e.Handled = true;
    }

    private void TodoTab_Click(object sender, RoutedEventArgs e)
    {
        SelectFunction(FunctionTab.Todo);
        e.Handled = true;
    }

    private void TranslateTab_Click(object sender, RoutedEventArgs e)
    {
        SelectFunction(FunctionTab.Translate);
        e.Handled = true;
    }

    private void AgentTab_Click(object sender, RoutedEventArgs e)
    {
        SelectFunction(FunctionTab.Agent);
        AgentInputBox.Focus();
        e.Handled = true;
    }

    private bool IsTranslationTypingHoldActive
        => _editingTranslation && DateTime.UtcNow < _translationTypingHoldUntil;

    private bool IsAgentTypingHoldActive
        => _editingAgent && DateTime.UtcNow < _agentTypingHoldUntil;

    private void TranslationInput_GotFocus(object sender, RoutedEventArgs e)
    {
        _editingTranslation = true;
        _translationTypingHoldUntil = DateTime.MinValue;
        _collapseTimer.Stop();
    }

    private void TranslationInput_LostFocus(object sender, RoutedEventArgs e)
    {
        _editingTranslation = false;
        _translationTypingHoldUntil = DateTime.MinValue;

        if (_state == IslandVisualState.Expanded && !_hovering && !_pinned && !_menuOpen)
        {
            ScheduleCollapse();
        }
    }

    private void TranslationInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TranslationInputBox.IsKeyboardFocused)
        {
            ExtendTranslationTypingHold();
        }
    }

    private void ExtendTranslationTypingHold()
    {
        _translationTypingHoldUntil = DateTime.UtcNow.AddSeconds(2);
        _collapseTimer.Stop();

        if (_state == IslandVisualState.Expanded)
        {
            _typingHoldTimer.Start();
        }
    }

    private void AgentInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (AgentInputBox.IsKeyboardFocused)
        {
            ExtendAgentTypingHold();
        }
    }

    private void ExtendAgentTypingHold()
    {
        _agentTypingHoldUntil = DateTime.UtcNow.AddSeconds(2);
        _collapseTimer.Stop();

        if (_state == IslandVisualState.Expanded)
        {
            _typingHoldTimer.Start();
        }
    }

    private void TypingHoldTimer_Tick(object? sender, EventArgs e)
    {
        if (IsAgentTypingHoldActive || IsTranslationTypingHoldActive)
        {
            return;
        }

        _typingHoldTimer.Stop();

        if (_state == IslandVisualState.Expanded
            && !_hovering
            && !_pinned
            && !_editingTodo
            && !_menuOpen
            && !_dragActive
            && !_dragOutActive)
        {
            ScheduleCollapse();
        }
    }

    private void AgentInput_GotFocus(object sender, RoutedEventArgs e)
    {
        _editingAgent = true;
        _agentTypingHoldUntil = DateTime.MinValue;
        _collapseTimer.Stop();
    }

    private void AgentInput_LostFocus(object sender, RoutedEventArgs e)
    {
        _editingAgent = false;
        _agentTypingHoldUntil = DateTime.MinValue;

        if (_state == IslandVisualState.Expanded && !_hovering && !_pinned && !_menuOpen)
        {
            ScheduleCollapse();
        }
    }

    private void TranslationTarget_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CycleTranslationTarget();
        e.Handled = true;
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _viewModel.TranslateAsync();
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.TryCopyTranslation();
        e.Handled = true;
    }

    private async void TranslationInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ExtendTranslationTypingHold();

        if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        e.Handled = true;
        await _viewModel.TranslateAsync();
    }

    private async void AgentSend_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SendAgentMessageAsync();
    }

    private async void AgentInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ExtendAgentTypingHold();

        if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        e.Handled = true;
        await SendAgentMessageAsync();
    }

    private async Task SendAgentMessageAsync()
    {
        await _viewModel.SendAgentMessageAsync();
        AgentInputBox.Focus();
    }

    private void AgentPaste_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PasteClipboardToAgent();
        AgentInputBox.Focus();
        e.Handled = true;
    }

    private void AgentCopy_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.TryCopyAgentReply();
        e.Handled = true;
    }

    private void AgentClear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearAgentChat();
        AgentInputBox.Focus();
        e.Handled = true;
    }

    private void AgentMessages_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                AgentScroll.ScrollToEnd();
            }));
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddTodo();
        TodoInput.Focus();
        e.Handled = true;
    }

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _viewModel.AddTodo();
        TodoInput.Focus();
        e.Handled = true;
    }

    private void TodoText_GotFocus(object sender, RoutedEventArgs e)
    {
        // Editing keeps the island open temporarily, but does not change the
        // explicit pin state. The dedicated footer button owns pinning now.
        _editingTodo = true;
        _collapseTimer.Stop();
    }

    private void TodoText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is TextBox { DataContext: TodoItemViewModel item })
        {
            _viewModel.NormalizeTodoText(item);
        }

        TodoInput.Focus();
        e.Handled = true;
    }

    private void TodoText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TodoItemViewModel item })
        {
            _viewModel.NormalizeTodoText(item);
        }

        // Focus can move between the input and a row; only leave editing mode
        // after focus has actually left the whole todo panel.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (TodoPanel.IsKeyboardFocusWithin)
                {
                    return;
                }

                _editingTodo = false;

                if (!_pinned && !_hovering && _state == IslandVisualState.Expanded)
                {
                    ScheduleCollapse();
                }
            }));
    }

    private void TodoCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check
            || check.DataContext is not TodoItemViewModel item
            || !_completingTodoIds.Add(item.Id))
        {
            return;
        }

        item.IsCompleting = true;
        var origin = new Point(18, 18);

        try
        {
            origin = check.TransformToVisual(TodoParticleCanvas)
                .Transform(new Point(check.ActualWidth / 2, check.ActualHeight / 2));
        }
        catch (InvalidOperationException)
        {
            // Use the fallback origin if the row is already being detached.
        }

        SpawnTodoParticles(origin);
        AnimateTodoRowOut(check);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(340)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _viewModel.CompleteTodo(item);
            _completingTodoIds.Remove(item.Id);
        };
        timer.Start();
    }

    private void SpawnTodoParticles(Point origin)
    {
        var palette = new[]
        {
            (Brush)FindResource("AccentBrush"),
            (Brush)FindResource("AccentBlueBrush"),
            (Brush)FindResource("TextPrimaryBrush")
        };

        for (var i = 0; i < 24; i++)
        {
            var size = 2.5 + (Random.Shared.NextDouble() * 3.5);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = palette[i % palette.Length],
                Opacity = 1
            };

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var startRadius = i < 8 ? 5 + (Random.Shared.NextDouble() * 12) : 0;
            var translate = new TranslateTransform();
            dot.RenderTransform = translate;
            Canvas.SetLeft(dot, origin.X + (Math.Cos(angle) * startRadius) - (size / 2));
            Canvas.SetTop(dot, origin.Y + (Math.Sin(angle) * startRadius) - (size / 2));
            TodoParticleCanvas.Children.Add(dot);

            var distance = 32 + (Random.Shared.NextDouble() * 58);
            var dx = Math.Cos(angle) * distance;
            var dy = (Math.Sin(angle) * distance) - 24;
            var duration = TimeSpan.FromMilliseconds(460 + Random.Shared.Next(320));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, dx, duration) { EasingFunction = ease });
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, dy, duration) { EasingFunction = ease });

            var fade = new DoubleAnimation(1, 0, duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(50)
            };
            fade.Completed += (_, _) => TodoParticleCanvas.Children.Remove(dot);
            dot.BeginAnimation(OpacityProperty, fade);
        }

        var ring = new Ellipse
        {
            Width = 22,
            Height = 22,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            Opacity = 0.9,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var ringScale = new ScaleTransform(0.5, 0.5);
        ring.RenderTransform = ringScale;
        Canvas.SetLeft(ring, origin.X - 11);
        Canvas.SetTop(ring, origin.Y - 11);
        TodoParticleCanvas.Children.Add(ring);

        var ringDuration = TimeSpan.FromMilliseconds(420);
        var ringEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        ringScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 2.8, ringDuration) { EasingFunction = ringEase });
        ringScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 2.8, ringDuration) { EasingFunction = ringEase });

        var ringFade = new DoubleAnimation(0.9, 0, ringDuration);
        ringFade.Completed += (_, _) => TodoParticleCanvas.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, ringFade);
    }

    private void AnimateTodoRowOut(CheckBox check)
    {
        var row = FindTodoRow(check);
        if (row is null)
        {
            return;
        }

        var scale = new ScaleTransform(1, 1);
        row.RenderTransformOrigin = new Point(0, 0.5);
        row.RenderTransform = scale;

        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        row.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, duration) { EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.92, duration) { EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.92, duration) { EasingFunction = ease });
    }

    private static Border? FindTodoRow(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Border { Tag: "TodoRow" } row)
            {
                return row;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
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

        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragOutActive = false;

            if (!_hovering && !_pinned && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive)
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

    private static bool HasFilePayload(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
           || data.GetDataPresent("FileNameW")
           || data.GetDataPresent("FileName")
           || ShellIdListParser.HasPayload(data)
           || ShellFileGroupParser.HasPayload(data);

    private bool AcceptsDropPayload(IDataObject data)
        => HasFilePayload(data)
           || _startMenuOpen
           || DateTime.UtcNow < _startMenuDragGraceUntil;

    private static IReadOnlyList<string> ExtractDroppedPaths(IDataObject data)
    {
        // FileDrop is the normal OLE format. FileNameW/FileName are fallbacks used
        // by a few drag sources. Start menu drags commonly expose Shell IDList
        // Array instead, which is resolved to real .lnk paths by the parser.
        foreach (var format in new[] { DataFormats.FileDrop, "FileNameW", "FileName" })
        {
            var paths = ParseDropPayload(data.GetData(format));
            if (paths.Count > 0)
            {
                return paths;
            }
        }

        var shellPaths = ShellIdListParser.Extract(data);
        if (shellPaths.Count > 0)
        {
            return shellPaths;
        }

        var fileGroupPaths = ShellFileGroupParser.Extract(data);
        if (fileGroupPaths.Count > 0)
        {
            return fileGroupPaths;
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

        if (!_hovering && !_pinned && !_editingTodo && !IsTranslationTypingHoldActive && !IsAgentTypingHoldActive && _state == IslandVisualState.Expanded)
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

        if (_hovering || _pinned || _editingTodo || IsTranslationTypingHoldActive || IsAgentTypingHoldActive || _menuOpen || _dragActive || _dragOutActive
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
        var previousState = _state;
        var changed = _state != state;
        _state = state;

        if (state != IslandVisualState.Expanded)
        {
            _editingTodo = false;
            _editingTranslation = false;
            _editingAgent = false;
            _translationTypingHoldUntil = DateTime.MinValue;
            _agentTypingHoldUntil = DateTime.MinValue;
            _typingHoldTimer.Stop();
        }

        _viewModel.IsExpanded = state == IslandVisualState.Expanded;
        _viewModel.IsNotification = state == IslandVisualState.Notification;
        _viewModel.IsPinned = _pinned && state == IslandVisualState.Expanded;

        var shouldAnimate = animate && _loaded && _settings.AnimationsEnabled;
        PositionWindow(shouldAnimate);

        if (changed || _activeView != GetView(state))
        {
            if (shouldAnimate
                && changed
                && state is IslandVisualState.Compact or IslandVisualState.Expanded
                && previousState is IslandVisualState.Compact or IslandVisualState.Expanded)
            {
                BeginContentMask(expanding: state == IslandVisualState.Expanded);
            }
            else
            {
                EndContentMask();
            }

            SwitchContent(state, shouldAnimate);

            if (shouldAnimate && changed)
            {
                StartTransitionBlur(
                    peakRadius: state == IslandVisualState.Compact ? 4.5 : 3.5,
                    durationMs: 260,
                    beginMs: state == IslandVisualState.Compact ? 20 : 0);
            }
        }
    }

    private void BeginContentMask(bool expanding)
    {
        _contentMaskActive = true;
        _contentMaskExpanding = expanding;
        _contentMaskGeometry ??= new RectangleGeometry();
        ContentRoot.Clip = _contentMaskGeometry;
    }

    private void EndContentMask()
    {
        _contentMaskActive = false;
        ContentRoot.Clip = null;
        _contentMaskGeometry = null;
    }

    private void ApplyContentMask(double width, double height, double progress)
    {
        if (!_contentMaskActive || _contentMaskGeometry is null)
        {
            return;
        }

        var eased = Math.Clamp(progress, 0.0, 1.0);
        var reveal = _contentMaskExpanding
            ? 0.16 + (0.84 * eased)
            : 1.0 - (0.84 * eased);
        var maskHeight = Math.Max(1.0, height * reveal);
        var radius = Math.Min(22.0, maskHeight / 2.0);
        _contentMaskGeometry.Rect = new Rect(0, 0, Math.Max(1.0, width), maskHeight);
        _contentMaskGeometry.RadiusX = radius;
        _contentMaskGeometry.RadiusY = radius;
    }

    private void PositionWindow(bool animate)
    {
        var screen = ResolveScreen();
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        var workWidth = Math.Max(1.0, bottomRight.X - topLeft.X);
        var workHeight = Math.Max(1.0, bottomRight.Y - topLeft.Y);

        var outer = IslandMetrics.Outer(_state, _viewModel.IsWorkspaceActive);
        outer = IslandMetrics.FitWithin(
            outer,
            workWidth - 24.0,
            workHeight - _settings.TopMargin - 8.0);

        var placement = IslandPlacement.TopCenter(
            topLeft.X,
            topLeft.Y,
            workWidth,
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

        var targetRadius = _state == IslandVisualState.Compact
            ? 23.0
            : _viewModel.IsWorkspaceActive ? 20.0 : 22.0;

        if (!animate || !_loaded)
        {
            _boundsAnimationId++;
            _heightSyncTimer.Stop();
            _nativeHeightEase = null;
            EndContentMask();
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            _cornerRadiusFrom = targetRadius;
            _cornerRadiusTo = targetRadius;
            ApplyCornerRadius(targetRadius);
            ApplyNativeHeight();
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

        var animationId = ++_boundsAnimationId;
        var collapsing = height < fromHeight - 1;
        EasingFunctionBase ease = collapsing
            ? new SpringEasingFunction { Damping = 1.0, Frequency = 1.7, EasingMode = EasingMode.EaseIn }
            : new SpringEasingFunction { Damping = 0.9, Frequency = 1.45, EasingMode = EasingMode.EaseIn };

        // Larger state changes (compact -> workspace) need a little more time;
        // small hover transitions stay snappy.
        var distance = Math.Abs(width - fromWidth) + Math.Abs(height - fromHeight);
        var durationMs = collapsing
            ? 380.0
            : Math.Min(460.0, 340.0 + (distance * 0.08));
        var duration = TimeSpan.FromMilliseconds(durationMs);

        // The native HWND size does not follow Window.Width/Height reliably
        // while those properties are animated on a transparent window, so
        // drive both native dimensions from the same timing curve explicitly.
        _nativeHeightStart = DateTime.UtcNow;
        _nativeWidthFrom = fromWidth;
        _nativeWidthTo = width;
        _nativeHeightFrom = fromHeight;
        _nativeHeightTo = height;
        _nativeHeightDuration = duration;
        _nativeHeightEase = ease;
        _cornerRadiusFrom = Shell.CornerRadius.TopLeft;
        _cornerRadiusTo = targetRadius;

        var widthAnimation = new DoubleAnimation(fromWidth, width, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Timeline.SetDesiredFrameRate(widthAnimation, 60);
        BeginAnimation(WidthProperty, widthAnimation);

        var heightAnimation = new DoubleAnimation(fromHeight, height, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Timeline.SetDesiredFrameRate(heightAnimation, 60);
        heightAnimation.Completed += (_, _) =>
        {
            if (animationId != _boundsAnimationId)
            {
                return;
            }

            _heightSyncTimer.Stop();
            _nativeHeightEase = null;
            BeginAnimation(HeightProperty, null);
            Height = height;
            EndContentMask();
            ApplyNativeHeight();
        };

        BeginAnimation(HeightProperty, heightAnimation);
        _heightSyncTimer.Start();

        var leftAnimation = new DoubleAnimation(fromLeft, left, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        Timeline.SetDesiredFrameRate(leftAnimation, 60);
        BeginAnimation(LeftProperty, leftAnimation);
    }

    /// <summary>
    /// WPF can defer native width/height updates while Window.Width/Height are
    /// animated. Keep both HWND dimensions in sync with the animated DIP
    /// values so the island does not snap or lag at the end of a transition.
    /// </summary>
    private void ApplyNativeHeight()
    {
        if (_hwndSource is null)
        {
            return;
        }

        var width = Width;
        var height = Height;
        if (_nativeHeightEase is not null)
        {
            var progress = Math.Clamp(
                (DateTime.UtcNow - _nativeHeightStart).TotalMilliseconds
                / _nativeHeightDuration.TotalMilliseconds,
                0.0,
                1.0);
            var eased = _nativeHeightEase.Ease(progress);
            width = _nativeWidthFrom + ((_nativeWidthTo - _nativeWidthFrom) * eased);
            height = _nativeHeightFrom + ((_nativeHeightTo - _nativeHeightFrom) * eased);
            ApplyCornerRadius(
                _cornerRadiusFrom + ((_cornerRadiusTo - _cornerRadiusFrom) * eased));
            ApplyContentMask(width, height, eased);
        }

        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        var handle = _hwndSource.Handle;
        if (!NativeMethods.GetWindowRect(handle, out _))
        {
            return;
        }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        var scaleX = transform?.M11 ?? 1.0;
        var scaleY = transform?.M22 ?? 1.0;
        var nativeWidth = Math.Max(1, (int)Math.Round(width * scaleX));
        var nativeHeight = Math.Max(1, (int)Math.Round(height * scaleY));

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            nativeWidth,
            nativeHeight,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void ApplyCornerRadius(double radius)
    {
        var clamped = Math.Clamp(radius, 16.0, 23.0);
        var corner = new CornerRadius(clamped);
        Shell.CornerRadius = corner;
        ShadowBorder.CornerRadius = corner;
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
        var collapsing = state == IslandVisualState.Compact
                         && ReferenceEquals(previous, ExpandedView);

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
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(collapsing ? 110 : 190))
                {
                    // Wait for the expanded content to finish fading before the
                    // compact clock appears; otherwise both layouts overlap.
                    BeginTime = TimeSpan.FromMilliseconds(collapsing ? 250 : 40),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            // Zoom out of the top edge: the island grows from the screen edge
            // instead of merely re-laying out at the new size. On collapse the
            // compact content arrives a little later so the expanded content
            // has time to fade instead of being clipped abruptly.
            var scaleFrom = ReferenceEquals(target, ExpandedView)
                ? 0.92
                : collapsing ? 0.94 : 0.95;
            var scaleEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var scaleDuration = TimeSpan.FromMilliseconds(collapsing ? 120 : 300);

            scale.ScaleX = 1;
            scale.ScaleY = 1;
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(scaleFrom, 1, scaleDuration)
                {
                    BeginTime = TimeSpan.FromMilliseconds(collapsing ? 240 : 0),
                    EasingFunction = scaleEase
                });
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(scaleFrom, 1, scaleDuration)
                {
                    BeginTime = TimeSpan.FromMilliseconds(collapsing ? 240 : 0),
                    EasingFunction = scaleEase
                });

            transform.Y = collapsing ? 4 : 6;
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(collapsing ? 140 : 240))
                {
                    BeginTime = TimeSpan.FromMilliseconds(collapsing ? 240 : 0),
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

            if (collapsing)
            {
                var (previousScale, previousTranslate) = GetContentTransforms(view);
                var exitDuration = TimeSpan.FromMilliseconds(220);
                var exitEase = new CubicEase { EasingMode = EasingMode.EaseIn };

                previousScale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1, 0.985, exitDuration) { EasingFunction = exitEase });
                previousScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1, 0.985, exitDuration) { EasingFunction = exitEase });
                previousTranslate.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(0, -4, exitDuration) { EasingFunction = exitEase });
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(collapsing ? 230 : 140))
            {
                BeginTime = TimeSpan.FromMilliseconds(collapsing ? 10 : 0),
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
