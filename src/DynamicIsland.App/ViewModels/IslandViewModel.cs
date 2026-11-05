using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.App.Interop;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Models;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;
using Size = System.Windows.Size;

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
    private readonly LocalTranslationService _localModelService = new();
    private readonly TodoStore _todoStore = new();
    private readonly ShortcutStore _shortcutStore = new();
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
    private FunctionTab _activeFunction = FunctionTab.Shortcuts;
    private bool _calendarLoading;
    private bool _calendarLoaded;
    private bool _calendarError;
    private string _calendarStatusText = "今日";
    private string _calendarErrorTitle = string.Empty;
    private string _calendarErrorMessage = string.Empty;
    private DateTime _calendarLoadedAt;
    private int _calendarRequestId;
    private readonly DispatcherTimer _calendarCountdownTimer;
    private bool _isMeetingCountdownActive;
    private string _countdownSubject = string.Empty;
    private string _countdownText = "05:00";
    private string _countdownStartText = string.Empty;
    private Geometry? _countdownRingGeometry;
    private string _translationInput = string.Empty;
    private string _translationOutput = string.Empty;
    private string _translationStatus = "本地模型 · 等待输入";
    private string _translationTarget = "English";
    private bool _isTranslating;
    private int _translationRequestId;
    private CancellationTokenSource? _translationCts;
    private string _agentInput = string.Empty;
    private string _agentStatus = "本地模型 · 等待提问";
    private bool _isAgentBusy;
    private int _agentRequestId;
    private CancellationTokenSource? _agentCts;
    private string _newTodoText = string.Empty;

    private const string AgentGreeting =
        "你好，我是本地 AI Agent。可以问我问题、整理思路、写代码或分析文本。所有请求只发送到本机模型。";

    private static readonly string[] TranslationTargets = ["中文", "English", "日本語", "한국어"];

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

        _calendarCountdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _calendarCountdownTimer.Tick += OnCalendarCountdownTick;

        AgentMessages.Add(new AgentMessageViewModel(AgentGreeting, isUser: false));

        RefreshClock();
        RefreshPower();
        RefreshScreen();
        LoadShortcuts();
        LoadTodos();
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

    public ObservableCollection<ShortcutSlotViewModel> ShortcutSlots { get; } = new(
        Enumerable.Range(0, ShortcutStore.SlotCount).Select(_ => new ShortcutSlotViewModel()));

    public string ShortcutCountText =>
        $"{ShortcutSlots.Count(slot => slot.HasShortcut)} / {ShortcutStore.SlotCount}";

    public ObservableCollection<TodoItemViewModel> Todos { get; } = new();

    public bool HasTodos => Todos.Count > 0;

    public bool IsTodoEmpty => Todos.Count == 0;

    public string TodoCountText => $"{Todos.Count} 个待办";

    public string NewTodoText
    {
        get => _newTodoText;
        set
        {
            if (SetProperty(ref _newTodoText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsTodoInputEmpty));
            }
        }
    }

    public bool IsTodoInputEmpty => string.IsNullOrWhiteSpace(NewTodoText);

    public string TranslationInput
    {
        get => _translationInput;
        set
        {
            if (SetProperty(ref _translationInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanTranslate));
                OnPropertyChanged(nameof(IsTranslationInputEmpty));
            }
        }
    }

    public bool IsTranslationInputEmpty => string.IsNullOrWhiteSpace(TranslationInput);

    public string TranslationOutput
    {
        get => _translationOutput;
        private set
        {
            if (SetProperty(ref _translationOutput, value))
            {
                OnPropertyChanged(nameof(HasTranslationOutput));
            }
        }
    }

    public bool HasTranslationOutput => !string.IsNullOrWhiteSpace(TranslationOutput);

    public string TranslationStatus
    {
        get => _translationStatus;
        private set => SetProperty(ref _translationStatus, value);
    }

    public string TranslationTargetLabel => $"→ {_translationTarget}";

    public bool IsTranslating => _isTranslating;

    public bool CanTranslate => !_isTranslating && !string.IsNullOrWhiteSpace(TranslationInput);

    public void CycleTranslationTarget()
    {
        var index = Array.IndexOf(TranslationTargets, _translationTarget);
        _translationTarget = TranslationTargets[(index + 1) % TranslationTargets.Length];
        OnPropertyChanged(nameof(TranslationTargetLabel));
    }

    public async Task TranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationInput))
        {
            TranslationStatus = "请输入要翻译的文字";
            return;
        }

        var requestId = ++_translationRequestId;
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _translationCts = cts;

        _isTranslating = true;
        OnPropertyChanged(nameof(IsTranslating));
        OnPropertyChanged(nameof(CanTranslate));
        TranslationOutput = string.Empty;
        TranslationStatus = "正在调用本地模型…";

        try
        {
            var result = await _localModelService.TranslateAsync(
                new TranslationRequest(
                    TranslationInput,
                    _translationTarget,
                    _settings.LocalLlmEndpoint,
                    _settings.LocalLlmModel),
                cts.Token);

            if (requestId != _translationRequestId)
            {
                return;
            }

            TranslationOutput = result.Text;
            TranslationStatus = $"完成 · {result.Provider} · {result.Model}";
        }
        catch (OperationCanceledException) when (requestId == _translationRequestId)
        {
            TranslationStatus = "已取消";
        }
        catch (Exception ex) when (requestId == _translationRequestId)
        {
            TranslationStatus = ex.Message;
        }
        finally
        {
            if (requestId == _translationRequestId)
            {
                _isTranslating = false;
                _translationCts = null;
                cts.Dispose();
                OnPropertyChanged(nameof(IsTranslating));
                OnPropertyChanged(nameof(CanTranslate));
            }
        }
    }

    public bool TryCopyTranslation()
    {
        if (string.IsNullOrWhiteSpace(TranslationOutput))
        {
            return false;
        }

        try
        {
            System.Windows.Clipboard.SetText(TranslationOutput);
            TranslationStatus = "已复制到剪贴板";
            return true;
        }
        catch (Exception ex)
        {
            TranslationStatus = $"复制失败：{ex.Message}";
            return false;
        }
    }

    public void CancelTranslation()
    {
        _translationRequestId++;
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = null;
        _isTranslating = false;
        OnPropertyChanged(nameof(IsTranslating));
        OnPropertyChanged(nameof(CanTranslate));
    }

    public string AgentInput
    {
        get => _agentInput;
        set
        {
            var text = value ?? string.Empty;
            if (text.Length > 4000)
            {
                text = text[..4000];
            }

            if (SetProperty(ref _agentInput, text))
            {
                OnPropertyChanged(nameof(CanSendAgent));
                OnPropertyChanged(nameof(IsAgentInputEmpty));
            }
        }
    }

    public bool IsAgentInputEmpty => string.IsNullOrWhiteSpace(AgentInput);

    public string AgentStatus
    {
        get => _agentStatus;
        private set => SetProperty(ref _agentStatus, value);
    }

    public bool IsAgentBusy => _isAgentBusy;

    public bool CanSendAgent => !_isAgentBusy && !string.IsNullOrWhiteSpace(AgentInput);

    public async Task SendAgentMessageAsync()
    {
        var text = AgentInput.Trim();
        if (text.Length == 0 || _isAgentBusy)
        {
            return;
        }

        AgentInput = string.Empty;
        AgentMessages.Add(new AgentMessageViewModel(text, isUser: true));

        var requestId = ++_agentRequestId;
        _agentCts?.Cancel();
        _agentCts?.Dispose();
        var cts = new CancellationTokenSource();
        _agentCts = cts;

        _isAgentBusy = true;
        OnPropertyChanged(nameof(IsAgentBusy));
        OnPropertyChanged(nameof(CanSendAgent));
        AgentStatus = "AI Agent 正在思考…";

        try
        {
            var messages = new List<LocalChatMessage>
            {
                new("system", BuildAgentSystemPrompt())
            };

            foreach (var message in AgentMessages.Skip(1).TakeLast(18))
            {
                messages.Add(new LocalChatMessage(
                    message.IsUser ? "user" : "assistant",
                    message.Text));
            }

            var result = await _localModelService.CompleteAsync(
                messages,
                _settings.LocalLlmEndpoint,
                _settings.LocalLlmModel,
                cts.Token);

            if (requestId != _agentRequestId)
            {
                return;
            }

            AgentMessages.Add(new AgentMessageViewModel(result.Text, isUser: false));
            AgentStatus = $"完成 · {result.Provider} · {result.Model}";
        }
        catch (OperationCanceledException) when (requestId == _agentRequestId)
        {
            AgentStatus = "已取消";
        }
        catch (Exception ex) when (requestId == _agentRequestId)
        {
            AgentStatus = ex.Message;
        }
        finally
        {
            if (requestId == _agentRequestId)
            {
                _isAgentBusy = false;
                _agentCts = null;
                cts.Dispose();
                OnPropertyChanged(nameof(IsAgentBusy));
                OnPropertyChanged(nameof(CanSendAgent));
            }
        }
    }

    public void ClearAgentChat()
    {
        CancelAgent();
        AgentMessages.Clear();
        AgentMessages.Add(new AgentMessageViewModel(AgentGreeting, isUser: false));
        AgentInput = string.Empty;
        AgentStatus = "本地模型 · 等待提问";
    }

    public bool TryCopyAgentReply()
    {
        var reply = AgentMessages.LastOrDefault(message => !message.IsUser);
        if (reply is null)
        {
            return false;
        }

        try
        {
            System.Windows.Clipboard.SetText(reply.Text);
            AgentStatus = "已复制最后一条回复";
            return true;
        }
        catch (Exception ex)
        {
            AgentStatus = $"复制失败：{ex.Message}";
            return false;
        }
    }

    public bool PasteClipboardToAgent()
    {
        try
        {
            var text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                AgentStatus = "剪贴板没有文字";
                return false;
            }

            AgentInput = text;
            AgentStatus = "已粘贴剪贴板内容";
            return true;
        }
        catch (Exception ex)
        {
            AgentStatus = $"读取剪贴板失败：{ex.Message}";
            return false;
        }
    }

    public void CancelAgent()
    {
        _agentRequestId++;
        _agentCts?.Cancel();
        _agentCts?.Dispose();
        _agentCts = null;
        _isAgentBusy = false;
        OnPropertyChanged(nameof(IsAgentBusy));
        OnPropertyChanged(nameof(CanSendAgent));
    }

    private static string BuildAgentSystemPrompt()
    {
        return "You are \"灵动岛 AI Agent\", a helpful local desktop assistant. "
               + "Answer in the user's language. Be concise but complete. "
               + "You can help with writing, coding, analysis, planning, summarization and translation. "
               + "You cannot execute Windows commands, access local files, or control apps; "
               + "when a task requires that, explain the exact steps instead. "
               + "Plain text and Markdown are supported. "
               + $"Current local time: {DateTime.Now:yyyy-MM-dd HH:mm}.";
    }

    public FunctionTab ActiveFunction => _activeFunction;

    public bool IsShortcutsActive => _activeFunction == FunctionTab.Shortcuts;

    public bool IsCalendarActive => _activeFunction == FunctionTab.Calendar;

    public bool IsTodoActive => _activeFunction == FunctionTab.Todo;

    public bool IsTranslateActive => _activeFunction == FunctionTab.Translate;

    public bool IsAgentActive => _activeFunction == FunctionTab.Agent;

    public bool IsWorkspaceActive => IsTranslateActive || IsAgentActive;

    public ObservableCollection<AgentMessageViewModel> AgentMessages { get; } = new();

    public string ActiveFunctionTitle => _activeFunction switch
    {
        FunctionTab.Shortcuts => "常用软件",
        FunctionTab.Calendar => "Calendar",
        FunctionTab.Todo => "待办事项",
        FunctionTab.Translate => "翻译",
        FunctionTab.Agent => "AI Agent",
        _ => "功能"
    };

    public string FooterHint => _activeFunction switch
    {
        FunctionTab.Shortcuts => "拖入快捷方式 · 悬停选择 · 单击打开",
        FunctionTab.Calendar => "今日日程 · 点击圆点切换",
        FunctionTab.Todo => "输入待办 · 打勾完成 · 自动删除",
        FunctionTab.Translate => "本地模型翻译 · 悬停保持展开 · 移出后收起",
        FunctionTab.Agent => "本地 AI Agent · 对话只发送到本机模型",
        _ => "点击圆点切换"
    };

    public ObservableCollection<CalendarEventItem> CalendarEvents { get; } = new();

    public bool IsCalendarLoading => _calendarLoading;

    public bool HasCalendarError => _calendarError;

    public bool HasCalendarEvents => CalendarEvents.Count > 0;

    public bool IsCalendarEmpty => _calendarLoaded && !_calendarLoading && !_calendarError
                                   && CalendarEvents.Count == 0;

    public bool IsMeetingCountdownActive => _isMeetingCountdownActive;

    public string CountdownSubject => _countdownSubject;

    public string CountdownText => _countdownText;

    public string CountdownStartText => _countdownStartText;

    public Geometry? CountdownRingGeometry => _countdownRingGeometry;

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
                OnPropertyChanged(nameof(PinButtonText));
            }
        }
    }

    public bool IsNotification
    {
        get => _isNotification;
        set => SetProperty(ref _isNotification, value);
    }

    public string PinButtonText => IsPinned ? "取消固定" : "固定窗口";

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
        _calendarCountdownTimer.Start();

        if (!string.IsNullOrWhiteSpace(_settings.WebCalendarUrl))
        {
            _ = EnsureCalendarLoadedAsync();
        }
    }

    public void Stop()
    {
        _clockTimer.Stop();
        _powerTimer.Stop();
        _calendarCountdownTimer.Stop();
        CancelTranslation();
        CancelAgent();
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

    private void LoadShortcuts()
    {
        var saved = _shortcutStore.Load();
        for (var i = 0; i < ShortcutSlots.Count && i < saved.Count; i++)
        {
            if (saved[i] is { } path)
            {
                ShortcutSlots[i].TrySetShortcut(path);
            }
        }

        RaiseShortcutsChanged();
    }

    /// <summary>
    /// Pins one or more shortcuts. A drop on an occupied slot replaces that slot;
    /// remaining paths fill the first empty slots.
    /// </summary>
    public int PinShortcuts(
        IEnumerable<string> candidates,
        ShortcutSlotViewModel? preferredSlot = null)
    {
        var paths = new List<string>();
        foreach (var candidate in candidates)
        {
            if (ShortcutPath.TryNormalize(candidate, out var path)
                && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            return 0;
        }

        var pinned = 0;
        foreach (var path in paths)
        {
            var slot = preferredSlot ?? ShortcutSlots.FirstOrDefault(item => item.IsEmpty);
            if (slot is null)
            {
                break;
            }

            if (slot.TrySetShortcut(path))
            {
                pinned++;
                preferredSlot = null;
            }
        }

        if (pinned > 0)
        {
            RaiseShortcutsChanged();
            SaveShortcuts();
        }

        return pinned;
    }

    public void RemoveShortcut(ShortcutSlotViewModel slot)
    {
        slot.Clear();
        RaiseShortcutsChanged();
        SaveShortcuts();
    }

    private void RaiseShortcutsChanged()
    {
        OnPropertyChanged(nameof(ShortcutCountText));
    }

    private void SaveShortcuts()
    {
        if (!_shortcutStore.TrySave(ShortcutSlots.Select(slot => slot.Path), out var error))
        {
            AppLogger.Error($"Unable to save shortcuts: {error}");
        }
    }

    private void LoadTodos()
    {
        foreach (var record in _todoStore.Load())
        {
            Todos.Add(new TodoItemViewModel(record.Id, record.Text, record.CreatedAt));
        }

        RaiseTodosChanged();
    }

    public bool AddTodo()
    {
        var text = NewTodoText.Trim();
        if (string.IsNullOrWhiteSpace(text) || Todos.Count >= 20)
        {
            return false;
        }

        if (text.Length > 120)
        {
            text = text[..120];
        }

        Todos.Add(new TodoItemViewModel(
            Guid.NewGuid().ToString("N"),
            text,
            DateTime.Now));
        NewTodoText = string.Empty;
        RaiseTodosChanged();
        SaveTodos();
        return true;
    }

    public void NormalizeTodoText(TodoItemViewModel item)
    {
        item.Text = (item.Text ?? string.Empty).Trim();
        if (item.Text.Length > 120)
        {
            item.Text = item.Text[..120];
        }

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            if (Todos.Remove(item))
            {
                RaiseTodosChanged();
                SaveTodos();
            }

            return;
        }

        SaveTodos();
    }

    public void CompleteTodo(TodoItemViewModel item)
    {
        if (Todos.Remove(item))
        {
            RaiseTodosChanged();
            SaveTodos();
        }
    }

    private void RaiseTodosChanged()
    {
        OnPropertyChanged(nameof(HasTodos));
        OnPropertyChanged(nameof(IsTodoEmpty));
        OnPropertyChanged(nameof(TodoCountText));
    }

    private void SaveTodos()
    {
        var records = Todos.Select(item => new TodoRecord
        {
            Id = item.Id,
            Text = item.Text,
            CreatedAt = item.CreatedAt
        });

        if (!_todoStore.TrySave(records, out var error))
        {
            AppLogger.Error($"Unable to save todos: {error}");
        }
    }

    public void SelectFunction(FunctionTab function)
    {
        if (_activeFunction != function)
        {
            _activeFunction = function;
            OnPropertyChanged(nameof(ActiveFunction));
            OnPropertyChanged(nameof(IsShortcutsActive));
            OnPropertyChanged(nameof(IsCalendarActive));
            OnPropertyChanged(nameof(IsTodoActive));
            OnPropertyChanged(nameof(IsTranslateActive));
            OnPropertyChanged(nameof(IsAgentActive));
            OnPropertyChanged(nameof(IsWorkspaceActive));
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
        UpdateMeetingCountdown();

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
                    result.ErrorTitle ?? "无法读取 Calendar",
                    result.ErrorMessage ?? "请确认 Outlook 已安装并登录。");
                return;
            }

            foreach (var calendarEvent in result.Events)
            {
                CalendarEvents.Add(calendarEvent);
            }

            _calendarLoaded = true;
            _calendarLoadedAt = DateTime.Now;
            CalendarStatusText = DescribeCalendarStatus();
            UpdateMeetingCountdown();

            OnPropertyChanged(nameof(HasCalendarEvents));
            OnPropertyChanged(nameof(IsCalendarEmpty));
        }
        catch (Exception ex)
        {
            SetCalendarError("无法读取 Calendar", ex.Message);
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

    private void OnCalendarCountdownTick(object? sender, EventArgs e)
        => UpdateMeetingCountdown();

    private void UpdateMeetingCountdown()
    {
        if (!_calendarLoaded || _calendarLoading || _calendarError)
        {
            DeactivateMeetingCountdown();
            return;
        }

        var now = DateTime.Now;
        var next = CalendarEvents
            .Where(item => !item.IsAllDay && item.Start > now)
            .OrderBy(item => item.Start)
            .FirstOrDefault();

        if (next is null)
        {
            DeactivateMeetingCountdown();
            return;
        }

        var remaining = next.Start - now;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromMinutes(5))
        {
            DeactivateMeetingCountdown();
            return;
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        var subject = next.Subject;
        var startText = $"{next.Start:HH:mm} 开始";
        var text = $"{seconds / 60}:{seconds % 60:00}";
        var progress = Math.Clamp(seconds / 300.0, 0.0, 1.0);

        var newlyActive = !_isMeetingCountdownActive;
        _isMeetingCountdownActive = true;
        _countdownSubject = subject;
        _countdownText = text;
        _countdownStartText = startText;
        _countdownRingGeometry = BuildCountdownRing(progress);

        if (newlyActive)
        {
            OnPropertyChanged(nameof(IsMeetingCountdownActive));
            CalendarStatusText = "即将开始";
        }

        OnPropertyChanged(nameof(CountdownSubject));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(CountdownStartText));
        OnPropertyChanged(nameof(CountdownRingGeometry));
    }

    private void DeactivateMeetingCountdown()
    {
        if (!_isMeetingCountdownActive)
        {
            return;
        }

        _isMeetingCountdownActive = false;
        OnPropertyChanged(nameof(IsMeetingCountdownActive));

        if (!_calendarLoading && !_calendarError)
        {
            CalendarStatusText = DescribeCalendarStatus();
        }
    }

    private string DescribeCalendarStatus()
    {
        if (CalendarEvents.Count == 0)
        {
            return "今天没有日程";
        }

        return CalendarEvents.All(item => item.Subject == "（无主题）")
            ? "主题未公开"
            : $"{CalendarEvents.Count} 个日程";
    }

    private static Geometry BuildCountdownRing(double progress)
    {
        const double center = 45;
        const double radius = 37;
        var clamped = Math.Clamp(progress, 0.0, 1.0);
        var sweep = 359.999 * clamped;
        var start = PointOnCircle(center, radius, -90);
        var end = PointOnCircle(center, radius, -90 + sweep);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweep > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            center + (radius * Math.Cos(radians)),
            center + (radius * Math.Sin(radians)));
    }

    public void Dispose()
    {
        Stop();
        _clockTimer.Tick -= OnClockTick;
        _powerTimer.Tick -= OnPowerTick;
        _calendarCountdownTimer.Tick -= OnCalendarCountdownTick;
    }
}
