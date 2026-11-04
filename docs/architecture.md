# 架构与扩展点

## 1. 分层

```text
┌─────────────────────────────────────────────────────────────┐
│ DynamicIsland.App (WPF, net10.0-windows)                    │
│                                                             │
│  App.xaml.cs ── lifecycle / single instance / wiring        │
│      │                                                      │
│      ├── IslandWindow ────── animation, hit testing, menus  │
│      │      └── IslandViewModel ── clock, power, state       │
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

`IslandWindow` 不显示在任务栏，使用 `WS_EX_TOOLWINDOW` 和 `WS_EX_NOACTIVATE`，因此点击胶囊不会抢走当前应用焦点，托盘和设置窗口仍可正常交互。

## 3. 窗口与几何

### 尺寸

`IslandMetrics` 是几何的唯一来源（单位 DIP）：

| 状态 | 内容尺寸 | 外窗尺寸（含阴影留白） |
| --- | --- | --- |
| Compact | `248 × 46` | `276 × 74` |
| Expanded | `424 × 196` | `452 × 224` |
| Notification | `400 × 118` | `428 × 146` |

外窗四周各留 `14 DIP` 给 WPF 阴影。窗口本身是 `AllowsTransparency=True` 的透明分层窗口，视觉上只有胶囊本体是实心。

### 定位

`IslandPlacement.TopCenter` 根据显示器工作区（`Screen.WorkingArea`）计算水平居中位置和顶部间距。通过 `CompositionTarget.TransformFromDevice` 把物理像素转换成 DIP，兼容 Per-Monitor V2。

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
- 宽度和左边同时按相同缓动变化，视觉中心保持不动。
- 三个内容视图使用独立的 `Opacity` + `TranslateTransform` 交叉淡入；旧视图在 `140ms` 后折叠。
- `AnimationsEnabled=false` 时跳过所有动画，直接切到目标尺寸和内容。
- 窗口动画通过 `BeginAnimation` 临时覆盖基础值，`FillBehavior.Stop` 后落到目标基础值，避免重复点击导致动画叠加。

## 6. 系统集成

### 电源 / 系统信息

`GetSystemPowerStatus`（kernel32）读取电池百分比、交流电状态和是否真的存在电池。桌面机显示“交流电 / 已接通电源”，不会假装有电池百分比。

开机时长来自 `Environment.TickCount64`，标签为“已开机”。

### 托盘

`TrayIconService` 使用 WinForms `NotifyIcon` + `ContextMenuStrip`：

- 托盘菜单项：显示、设置、演示通知、开机启动、退出
- 自定义暗色 `ProfessionalColorTable`
- 图标从 `pack://application:,,,/Assets/app.ico` 加载，带 `SystemIcons.Application` 兜底

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
