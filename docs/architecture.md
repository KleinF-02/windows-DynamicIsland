# 架构与扩展点

## 1. 分层

```text
┌─────────────────────────────────────────────────────────────┐
│ DynamicIsland.App (WPF, net10.0-windows)                    │
│                                                             │
│  App.xaml.cs ── lifecycle / single instance / wiring        │
│      │                                                      │
│      ├── IslandWindow ────── animation, hit testing, menus  │
│      │      └── IslandViewModel ── clock, power, user, state  │
│      │                                                      │
│      ├── SettingsWindow ── SettingsViewModel                 │
│      ├── TrayIconService ─ NotifyIcon + context menu         │
│      └── StartupService ── HKCU Run key                     │
└──────────────────────────┬──────────────────────────────────┘
                           │ project reference
┌──────────────────────────▼──────────────────────────────────┐
│ DynamicIsland.Core (net10.0, no UI dependencies)             │
│                                                             │
│  IslandSettings      configuration model + Normalize()       │
│  IslandMetrics       compact / expanded / notification size  │
│  IslandPlacement     work-area top-center calculation        │
│  SettingsStore       JSON load / atomic save                 │
└─────────────────────────────────────────────────────────────┘
```

Core 不引用 WPF、WinForms 或任何窗口概念，因此几何和配置可以直接做单元测试，也可以被未来的 CLI、安装器或别的 UI 复用。

## 2. 应用生命周期

`App.OnStartup`（`ShutdownMode = OnExplicitShutdown`）：

1. 创建命名 Mutex，保证单实例。
2. 读取 `%LOCALAPPDATA%\DynamicIsland\settings.json`；首次运行写入默认值。
3. 同步当前用户启动项。
4. 创建 `IslandWindow` 和 `TrayIconService`。
5. `IslandWindow.Show()`；按参数进入 `--demo` / `--settings` / 首次运行欢迎通知。
6. 托盘“退出”或右键菜单“退出”才调用 `Shutdown()`。

`IslandWindow` 不显示在任务栏，使用 `WS_EX_TOOLWINDOW` 和 `ShowActivated=False`。启动、托盘唤出和悬停展开都不会抢焦点；点击胶囊后窗口才会激活，这是待办行内输入所必需的。设置窗口仍是普通可激活窗口。

## 3. 窗口与几何

### 尺寸

`IslandMetrics` 是几何的唯一来源（单位 DIP）：

| 状态 | 内容尺寸 | 外窗尺寸（含阴影留白） |
| --- | --- | --- |
| Compact | `248 × 46` | `276 × 74` |
| Expanded | `424 × 284` | `452 × 312` |
| Notification | `400 × 118` | `428 × 146` |

外窗四周各留 `14 DIP` 给 WPF 阴影。窗口本身是 `AllowsTransparency=True` 的透明分层窗口，视觉上只有胶囊本体是实心。

### 定位

`IslandPlacement.TopCenter` 根据显示器工作区（`Screen.WorkingArea`）计算水平居中位置。通过 `CompositionTarget.TransformFromDevice` 把物理像素转换成 DIP，兼容 Per-Monitor V2。

窗口顶部有 14 DIP 的透明阴影留白。`IslandWindow.PositionWindow` 会把窗口整体上移这段留白，因此 `TopMargin = 0` 时**胶囊视觉顶边正好贴住工作区顶部**，窗口的 `Top` 会是 `-14` 左右，顶部阴影被屏幕边缘裁掉。这是有意为之；否则会出现一条 14px 的视觉缝隙。

`TopMargin` 的语义是“到胶囊可见顶边的距离”，默认 `0`。设置页可调到 `0–40 DIP`，向右拉才会出现缝隙。

当前默认使用主屏。`IslandSettings.PreferredScreen` 已经存在，`ResolveScreen()` 也已按设备名匹配，只差设置页中的显示器下拉框。

### 点击穿透边距

窗口有 14 DIP 的透明阴影边距。`IslandWindow.WindowProc` 处理 `WM_NCHITTEST`：

- 光标在胶囊本体矩形内：正常命中。
- 光标落在透明边距：返回 `HTTRANSPARENT`，让点击落到下面的窗口。

圆角本身仍按矩形处理，误差只有几个像素；如果以后加入更大圆角，需要改成圆角命中检测。

## 4. 状态机

```text
             MouseEnter
   Compact ───────────────▶ Expanded
      ▲                        │
      │  MouseLeave + 280ms    │
      └────────────────────────┘

   Compact/Expanded ──Click──▶ pinned
   pinned ──Click────────────▶ unpinned

   Any ──ShowNotification───▶ Notification
   Notification ──4.5s / click──▶ Expanded（鼠标在岛上）或 Compact
```

- `_pinned` 与 `IslandViewModel.IsPinned` 同步；固定时只改变提示文案和收起条件。
- 右键菜单打开期间暂停收起计时，菜单关闭后重新判断。
- 通知态优先于普通展开；通知可直接复用 `ShowNotification`，后续媒体 / 日程只需新增调用方。

## 5. 动画

- 窗口 `Width`、`Height`、`Left` 同时使用 `QuinticEase(EaseOut)` 插值，`300ms`。
- 宽度和左边同时按相同缓动变化，视觉中心保持不动；顶部保持贴屏，因此展开只向下生长。
- 内容视图额外带 `ScaleTransform`：展开态从 `0.92` 放大到 `1.0`，`RenderTransformOrigin="0.5,0"` 让缩放从顶部中心发生。
- 三个内容视图使用独立的 `Opacity` + `TranslateTransform` 交叉淡入；旧视图在 `140ms` 后折叠。
- `AnimationsEnabled=false` 时跳过所有动画，直接切到目标尺寸和内容。
- 窗口动画通过 `BeginAnimation` 临时覆盖基础值，`FillBehavior.Stop` 后落到目标基础值，避免重复点击导致动画叠加。

## 6. 系统集成

### 电源

`GetSystemPowerStatus`（kernel32）读取电池百分比、交流电状态和是否真的存在电池。桌面机显示“交流电 / 已接通电源”，不会假装有电池百分比。

### 托盘

`TrayIconService` 使用 WinForms `NotifyIcon` + `ContextMenuStrip`：

- 托盘菜单项：显示、设置、演示通知、开机启动、退出
- 自定义暗色 `ProfessionalColorTable`
- 图标从 `pack://application:,,,/Assets/app.ico` 加载，带 `SystemIcons.Application` 兜底

### 功能区标签

展开态功能区有三个标签页，由底部的三个圆点按钮切换：

- `FunctionTab.FileShelf`：文件暂存
- `FunctionTab.Calendar`：Outlook 日历
- `FunctionTab.Todo`：待办事项

`IslandViewModel.ActiveFunction` 保存当前标签，派生出 `IsFileShelfActive`、`IsCalendarActive`、`IsTodoActive`、`ActiveFunctionTitle` 和 `FooterHint`。三个标签的内容使用同一块固定高度区域，圆点只切换可见性，不改变窗口尺寸。

### 文件暂存

文件暂存标签是一个文件架：

- `StagedFileViewModel` 保存路径、显示名、类型徽标、大小和“是否存在”。
- `IslandViewModel.AddFiles` 只接受真实存在的文件/文件夹；去重（忽略大小写）、最多 12 个、路径规范化。
- 拖入：`Shell` 开启 `AllowDrop`，`DragEnter/DragOver` 自动展开并高亮；`Drop` 兼容 `FileDrop`、`FileNameW`/`FileName` 以及部分 OLE 源返回的 `MemoryStream`（DROPFILES 结构）。
- 拖出：条目上按下鼠标后由 **Window 的 `PreviewMouseMove`** 统一判断拖动阈值，避免 `ScrollViewer` 捕获鼠标导致行内 `MouseMove` 丢失；`DoDragDrop` 只允许 `Copy`，因此原文件不会被移动或删除。
- `ExpandedView` 需要命中测试（文件行、清空、移除按钮），这是它和 Compact/Notification 视图不同的一点。
- 文件架是**会话内引用**，不持久化，不复制文件到应用目录；应用退出后列表消失，原文件始终不动。

### 待办事项

- `TodoItemViewModel` 保存 `Id / Text / CreatedAt / IsCompleting`；`IslandViewModel.Todos` 是 `ObservableCollection`。
- 顶部输入框绑定 `NewTodoText`，`Enter` 或 `+` 调用 `AddTodo`；回车使用 `PreviewKeyDown`，避免被 TextBox 的默认键盘处理吞掉。
- 每一行的文字本身是透明 TextBox，可直接点击修改；失焦或回车时 `NormalizeTodoText` 去空格、限制 120 字并保存。清空文字会删除该行。
- 打勾通过 `IsCompleting` 两向绑定触发 `TodoCheck_Checked`：代码后置在列表上方的 `Canvas` 中生成 24 个椭圆粒子和一个扩散圆环，同时让整行在 300 ms 内淡出并缩小；340 ms 后 `CompleteTodo` 从集合和 `todos.json` 删除。
- 粒子是 `TranslateTransform` + `DoubleAnimation`（CubicEase），完成后从 Canvas 移除；`ClipToBounds=True` 防止溢出功能区。
- `TodoStore` 使用 `%LOCALAPPDATA%\DynamicIsland\todos.json` 原子写入，最多 20 条，损坏文件回退为空。
- 输入框获得焦点时把胶囊标记为固定，避免鼠标移出后编辑中的列表收起。

### Outlook 日历

`OutlookCalendarService` 优先读取 **Microsoft 365 网页版发布的 ICS 链接**，未配置链接时才回退桌面 COM。

**网页 ICS 路径**

1. `IslandSettings.WebCalendarUrl` 非空时走网页路径；支持 `https://`、`webcal://`、`http(s)` 和本地 `.ics` 文件。
2. 通过静态 `HttpClient` 下载，20 秒超时、自动重定向/解压，内容上限 8 MB；如果返回 HTML 登录页则给出“链接可能已过期”的可读错误。
3. 使用 `Ical.Net.Calendar.Load` 解析 RFC 5545，并用 `GetOccurrences(today, tomorrow)` 展开重复日程。
4. 跳过 `STATUS:CANCELLED`，将 `Summary / Start / End / IsAllDay / Location` 映射为 `CalendarEventItem`。
5. ICS 链接包含私人令牌，只保存在本机设置里，不写入日志；不请求链接以外的服务。

**桌面 COM 回退**

1. 未配置网页链接时检查 `Outlook.Application` ProgID；不存在时返回“未配置日历来源”。
2. 在专用 STA 线程创建 `Outlook.Application`，通过 `GetNamespace("MAPI").GetDefaultFolder(9)` 读取默认日历。
3. 开启 `IncludeRecurrences`，按 `[Start]` 排序并用日期区间 `Restrict` 查询；如果当前 Outlook 不接受过滤表达式，则退回遍历并按托管代码过滤。
4. 每次读取最多 200 条；UI 侧 15 秒超时。失败时区分“未配置”“读取失败”并提供重试。

两条路径最终都返回同一套 `CalendarLoadResult`/`CalendarEventItem`，因此 UI 不区分来源。日历页不常驻定时器：首次切到该标签时读取，之后 2 分钟内复用结果，手动“刷新”会强制重新读取；修改设置里的 URL 会清空缓存并按需重新加载。

### 开机启动

`StartupService` 只写当前用户：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  DynamicIsland = "<exe>" --autostart
```

不需要管理员。开发阶段路径会指向 `bin\Debug\...`；正式使用应先发布，再从发布目录启动并重新开启，以写入稳定的路径。

### 单实例

命名 Mutex：`Local\DynamicIsland.App.SingleInstance`。第二个实例弹提示后退出；由开机启动拉起的重复实例静默退出。

## 7. 配置

`SettingsStore`：

- 路径：`%LOCALAPPDATA%\DynamicIsland\settings.json`
- `camelCase`、缩进 JSON
- 先写 `.tmp`，再 `File.Move(..., overwrite: true)`，避免半写文件
- 反序列化异常 / IO 异常 → 默认配置，绝不阻止启动
- `IslandSettings.Normalize()` 负责范围修复：`Opacity [0.60, 1.00]`，`TopMargin [0, 80]`

待办：`%LOCALAPPDATA%\DynamicIsland\todos.json`，由 `TodoStore` 原子写入；与设置文件分开，损坏时只丢待办，不影响启动。

日志：`%LOCALAPPDATA%\DynamicIsland\logs\app.log`，只记录启动、异常和关键系统错误，不记录鼠标轨迹。

## 8. 扩展点

### 增加一个设置项

1. `IslandSettings` 增加属性，并在 `Normalize()` 中约束。
2. `SettingsViewModel` 增加包装属性 + 通知。
3. `SettingsWindow.xaml` 增加开关 / 滑块。
4. 在 `IslandWindow.ApplySettings` 中消费。

### 增加一个展开态模块

1. 在 `IslandViewModel` 增加数据属性（不要直接操作窗口）。
2. 在 `ExpandedView` 的三列卡片区域增加内容，或改成新的布局行。
3. 如果模块高度变化，更新 `IslandMetrics.Content(Expanded)`，并补充尺寸测试。
4. 数据刷新优先使用 `DispatcherTimer`；网络调用放到后台线程后回 UI 线程赋值。

### 接入媒体控制

建议新增 `Services/MediaSessionService`，封装 WinRT：

```text
Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager
```

由服务暴露 `NowPlaying` 事件，`IslandViewModel` 订阅并更新标题 / 艺术家 / 播放状态。`Windows.Media.Control` 需要目标框架启用 Windows SDK 引用（`TargetFramework` 或 `Microsoft.Windows.SDK.Contracts`），不建议直接塞进窗口代码。

### 接入 Windows 通知

需要 `UserNotificationListener`（受限权限）或第三方通知镜像。建议单独做 `NotificationIngressService`，统一转换成：

```csharp
island.ShowNotification(title, message, duration);
```

这样 UI 不需要知道通知来源。

## 9. 已知技术债

- 设置页没有显示器选择；`PreferredScreen` 目前只能手改 JSON。
- 没有 UI 自动化测试；目前依赖 `--demo` + 手动 / 截图冒烟。
- 单文件 WPF 首次启动有原生库解压成本。
- 没有安装器、代码签名、自动更新。
- 阴影和圆角命中是矩形近似，极端点击位置可能差 1–2 px。
