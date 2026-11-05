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
      │  MouseLeave + 320ms    │
      └────────────────────────┘

   Compact ──Click──────────▶ Expanded（不固定）
   PinButton ──Click────────▶ pinned
   pinned ──PinButton───────▶ unpinned

   Any ──ShowNotification───▶ Notification
   Notification ──4.5s / click──▶ Expanded（鼠标在岛上）或 Compact
```

- 固定只能通过展开态右下角的 `PinButton` 切换，点击胶囊、添加待办或拖入文件都不会隐式固定。
- `_pinned` 与 `IslandViewModel.IsPinned` 同步；固定时只改变按钮文案和收起条件。
- 待办输入使用独立的 `_editingTodo` 状态：编辑期间鼠标移开不收起，但不会把窗口标记为固定。
- 每 `200ms` 检查前台进程；检测到 `StartMenuExperienceHost` / `SearchHost` 时，把岛收成 compact 胶囊并用 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 重新置顶，避免开始菜单盖住拖放目标。开始菜单关闭后，根据之前的固定/悬停状态恢复展开。
- 右键菜单打开期间暂停收起计时，菜单关闭后重新判断。
- 通知态优先于普通展开；通知可直接复用 `ShowNotification`，后续媒体 / 日程只需新增调用方。

## 5. 动画

- 展开：`Width`、`Height`、`Left` 使用 `QuinticEase(EaseOut)`，`300ms`。
- 收起：使用 `CubicEase(EaseInOut)`，`360ms`；鼠标离开后延迟 `320ms` 再开始，避免边缘误触。
- 透明分层窗口的 `Height` 在动画期间通过 `16ms` 原生 `SetWindowPos` 同步，避免 WPF 只在动画结束时才更新 HWND 高度、造成中段卡住或末尾跳动。
- 宽度和左边同时按相同缓动变化，视觉中心保持不动；顶部保持贴屏，因此展开只向下生长。
- 内容视图额外带 `ScaleTransform`：展开态从 `0.92` 放大到 `1.0`，`RenderTransformOrigin="0.5,0"` 让缩放从顶部中心发生。
- 收起时旧内容先缩小、上移并淡出（`220ms`），紧凑内容延迟 `95ms` 再淡入；窗口尺寸动画尚未结束时不会突然切换视图。
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

### 标签页

展开态有三个标签页，由底部的三个圆点按钮切换：

- `FunctionTab.Shortcuts`：常用软件
- `FunctionTab.Calendar`：Calendar
- `FunctionTab.Todo`：待办事项

`IslandViewModel.ActiveFunction` 保存当前标签，派生出 `IsShortcutsActive`、`IsCalendarActive`、`IsTodoActive`、`ActiveFunctionTitle` 和 `FooterHint`。三个标签的内容使用同一块固定高度区域，圆点只切换可见性，不改变窗口尺寸。

### 常用软件

- 固定 6 个 `ShortcutSlotViewModel`，每个保存路径、显示名、`ImageSource` 图标和空位状态。
- `ShortcutPath` 接受 `.lnk`、`.exe`、`.url`、`.appref-ms`、`.html`、`.htm`；`ShortcutStore` 把 6 个路径（含空位 null）原子写入 `shortcuts.json`。HTML 由 `UseShellExecute` 交给默认浏览器打开，图标通过 `SHGetFileInfo`（必要时回退 `ExtractAssociatedIcon`）读取默认浏览器图标；取不到时 XAML 用 `</>` 占位。
- 图标通过 `SHGetFileInfo(SHGFI_ICON | SHGFI_LARGEICON)` 获取，再由 `Imaging.CreateBitmapSourceFromHIcon` 转成 WPF 图像；结束后 `DestroyIcon` 释放句柄。
- 拖入：每个格子 `AllowDrop=True`。拖到空位填入该格，拖到已有格子替换；多文件依次填满剩余空位。`Shell_Drop` 作为岛内空白区域的兜底。
- 开始菜单通常不提供 `FileDrop`，而是提供 `Shell IDList Array`；`ShellIdListParser` 解析 CIDA 的 parent/child PIDL，用 `ILCombine` + `SHGetPathFromIDListW` 还原真实 `.lnk` 路径。虚拟 Store 应用没有文件路径时会被拒绝并提示改用桌面快捷方式。
- 悬停：使用 `BackEase` 把整个格子放大到 `1.16`，离场时平滑回到 `1.0`；只保留缩放动画。`EnsureMutableTransform` 会克隆 WPF 冻结的变换再动画。
- 单击快捷方式通过 `UseShellExecute` 启动；悬停后右上角 `×` 移除。右键仍保留全局菜单。
- 点击空位打开 `ShortcutPickerWindow`，枚举 `CommonStartMenu` / `StartMenu` 下的 `.lnk`，以及 `%LocalAppData%\Microsoft\WindowsApps` 下的应用执行别名（如 `SnippingTool.exe`），支持搜索、图标预览和选择后钉入；底部“浏览文件…”可用文件对话框直接选择 `.html` / `.htm` 或快捷方式；这是 Windows 11 开始菜单不提供拖拽源时的可用入口。
- 快捷方式路径保存在本机，不复制软件本身；不读取或上传快捷方式内容。

### 待办事项

- `TodoItemViewModel` 保存 `Id / Text / CreatedAt / IsCompleting`；`IslandViewModel.Todos` 是 `ObservableCollection`。
- 顶部输入框绑定 `NewTodoText`，`Enter` 或 `+` 调用 `AddTodo`；回车使用 `PreviewKeyDown`，避免被 TextBox 的默认键盘处理吞掉。
- 每一行的文字本身是透明 TextBox，可直接点击修改；失焦或回车时 `NormalizeTodoText` 去空格、限制 120 字并保存。清空文字会删除该行。
- 打勾通过 `IsCompleting` 两向绑定触发 `TodoCheck_Checked`：代码后置在列表上方的 `Canvas` 中生成 24 个椭圆粒子和一个扩散圆环，同时让整行在 300 ms 内淡出并缩小；340 ms 后 `CompleteTodo` 从集合和 `todos.json` 删除。
- 粒子是 `TranslateTransform` + `DoubleAnimation`（CubicEase），完成后从 Canvas 移除；`ClipToBounds=True` 防止溢出标签区域。
- `TodoStore` 使用 `%LOCALAPPDATA%\DynamicIsland\todos.json` 原子写入，最多 20 条，损坏文件回退为空。
- 输入框获得焦点时只设置 `_editingTodo`，避免鼠标移出后编辑中的列表收起；不会改变固定状态。焦点离开、窗口失活或切换到紧凑态时清除该状态，再按固定/悬停条件决定是否收起。

### 主题

`Themes/Palette.Dark.xaml` 与 `Themes/Palette.Light.xaml` 各自定义同一组颜色和画刷；`Themes/Theme.xaml` 只保留样式，并使用 `DynamicResource` 引用调色板。`ThemeService` 读取：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
  AppsUseLightTheme
```

启动时在 `ApplicationIdle` 应用目标调色板；监听 `SystemEvents.UserPreferenceChanged`，并用 2 秒轮询兜底，因此 Windows 切换深/浅色时已打开的灵动岛、设置页和选择器都会即时换色。`DYNAMIC_ISLAND_THEME=light|dark` 可用于本地截图测试，不写入用户设置。

### Calendar

`OutlookCalendarService` 优先读取 **Microsoft 365 网页版发布的 ICS 链接**，未配置链接时才回退桌面 COM。

**网页 ICS 路径**

1. `IslandSettings.WebCalendarUrl` 非空时走网页路径；支持 `https://`、`webcal://`、`http(s)` 和本地 `.ics` 文件。
2. 通过静态 `HttpClient` 下载，20 秒超时、自动重定向/解压，内容上限 8 MB；如果返回 HTML 登录页则给出“链接可能已过期”的可读错误。
3. 使用 `Ical.Net.Calendar.Load` 解析 RFC 5545，并用 `GetOccurrences(today, tomorrow)` 展开重复日程。
4. 跳过 `STATUS:CANCELLED`，将 `Summary / Start / End / IsAllDay / Location` 映射为 `CalendarEventItem`；如果发布源没有 `SUMMARY`，回退到 `DESCRIPTION` 的第一行，仍为空时显示“（无主题）”。所有主题都缺失时状态栏显示“主题未公开”。时间列使用独立的开始/结束两行时间块，开始时间加粗、结束时间弱化显示。
5. ICS 链接包含私人令牌，只保存在本机设置里，不写入日志；不请求链接以外的服务。

**桌面 COM 回退**

1. 未配置网页链接时检查 `Outlook.Application` ProgID；不存在时返回“未配置日历来源”。
2. 在专用 STA 线程创建 `Outlook.Application`，通过 `GetNamespace("MAPI").GetDefaultFolder(9)` 读取默认日历。
3. 开启 `IncludeRecurrences`，按 `[Start]` 排序并用日期区间 `Restrict` 查询；如果当前 Outlook 不接受过滤表达式，则退回遍历并按托管代码过滤。
4. 每次读取最多 200 条；UI 侧 15 秒超时。失败时区分“未配置”“读取失败”并提供重试。

两条路径最终都返回同一套 `CalendarLoadResult`/`CalendarEventItem`，因此 UI 不区分来源。首次切到日历标签时读取，之后 2 分钟内复用结果，手动“刷新”会强制重新读取；修改设置里的 URL 会清空缓存并按需重新加载。

会议倒计时由 `IslandViewModel` 的 100ms `DispatcherTimer` 驱动：取今天最近一场未开始、非全天的会议，若距开始时间 ≤ 5 分钟则进入倒计时；`CountdownRingGeometry` 用 `ArcSegment` 生成圆环进度，`CountdownText` 显示 `m:ss`。会议开始后条件不再成立，倒计时视口隐藏，日程列表恢复。全部主题为空时状态栏显示“主题未公开”，提示重新发布日历并选择“可查看所有详细信息”。

配置了网页 ICS 时，`Start()` 会后台预加载今日日历，因此不需要先打开 Calendar 标签；compact 胶囊和展开页绑定同一组 `IsMeetingCountdownActive / CountdownSubject / CountdownText / CountdownRingGeometry`，缩小后仍持续刷新。

### Translation

`LocalTranslationService` 只访问设置中的本机模型地址，不经过云端。

1. 默认地址 `http://127.0.0.1:11434` 优先走 Ollama `/api/chat`；`/v1` 或其他端口优先走 OpenAI 兼容 `/v1/chat/completions`。
2. 模型名留空时，Ollama 读取 `/api/tags`，OpenAI 兼容服务读取 `/v1/models`，取第一个可用模型。
3. 提示词要求只返回译文，保留换行、Markdown、代码、URL 和专有名词；温度 0.2。
4. 目标语言支持中文、English、日本語、한국어；UI 只保留输入/结果两个区域，Ctrl+Enter 触发，复制按钮写入剪贴板。
5. 请求支持取消：切换目标语言后重新翻译会取消旧请求；窗口停止时也会取消。超时 90 秒。
6. 进入翻译页时窗口临时置为 pinned 并强制展开，避免输入时鼠标移开被收起；离开翻译页时恢复进入前的 pinned 状态。固定按钮仍显示“取消固定”，用户可主动取消临时固定。
6. 常见失败包括服务未启动、没有加载模型、模型名错误和 HTTP 非 2xx；错误显示在翻译页状态文字中并写入本机日志。

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

快捷方式：`%LOCALAPPDATA%\DynamicIsland\shortcuts.json`，由 `ShortcutStore` 原子写入；空位保存为 null，最多 6 项。

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
