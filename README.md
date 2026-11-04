# 灵动岛 · Dynamic Island for Windows 11

> 一颗停靠在桌面顶部的胶囊。悬停展开，点击固定。

![灵动岛](docs/images/hero.png)

**当前状态：`0.1.0` 可运行开发骨架。** 这不是概念图：窗口、动画、托盘、设置、开机启动和通知胶囊都已在本机实机跑通。

## 现在能做什么

- 紧贴屏幕顶部的无边框透明胶囊，始终置顶，不占用任务栏
- 胶囊主标签显示当前 Windows 登录名（本机为 `klein`）
- 悬停自动放大展开，移开自动收起；点击固定 / 取消固定
- `300ms` 弹性尺寸动画 + 顶部中心缩放 + 内容交叉淡入
- 实时时钟、日期、秒数、电池 / 充电状态、屏幕分辨率、系统开机时长
- 通知胶囊：标题 + 正文 + 自动消失 / 点击关闭（当前由托盘和右键菜单触发演示）
- 深色设置页：置顶、动画、秒数、开机启动、不透明度、顶部距离（默认 `0` = 贴顶）
- 系统托盘图标与右键菜单
- 右键菜单：设置、演示通知、隐藏到托盘、退出
- 单实例运行；Alt+F4 默认隐藏到托盘而不是退出
- 配置 JSON 原子写入、启动 / 崩溃日志、开机启动注册表项

## 技术选型

| 层 | 选择 | 原因 |
| --- | --- | --- |
| 运行时 | **.NET 10 / WPF** | Windows 原生、单文件发布、无额外运行时依赖 |
| 窗口 | WPF 透明分层窗口 + Win32 样式 | 胶囊外形、逐帧尺寸动画和阴影都可控 |
| 系统集成 | Win32 P/Invoke + WinForms `NotifyIcon` | 托盘、电源状态、点击穿透边距，不引入第三方 UI 包 |
| 配置 | `System.Text.Json` + Core 层 | UI 无关、可单测、原子写入 |
| 测试 | xUnit | Core 层 10 个单元测试 |

运行时没有第三方 NuGet 依赖；xUnit 仅用于测试项目。

### 设置页

![设置页](docs/images/settings.png)

## 快速开始

### 1. 初始化开发环境

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
```

脚本会：

1. 检查 .NET 10 SDK；没有则用官方 `dotnet-install.ps1` **用户级安装**（不需要管理员）。
2. 把 SDK 写入用户 `PATH` 和 `DOTNET_ROOT`。
3. 还原依赖、构建解决方案、运行单元测试。

> 如果当前终端是在安装前打开的，请新开一个终端，让 `PATH` 和 `DOTNET_ROOT` 生效。

### 2. 运行

```powershell
# 带示例展开态启动，适合快速看效果
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Demo

# 直接打开设置页
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Settings

# 普通启动
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

也可以在 VS Code 中打开项目，按 `F5` 使用「灵动岛 (Debug + demo)」配置。

### 3. 发布独立 exe

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

默认产出 **win-x64、self-contained、single-file**：

```text
artifacts/publish/DynamicIsland-0.1.0-win-x64/DynamicIsland.exe
artifacts/publish/DynamicIsland-0.1.0-win-x64.zip
```

目标机器不需要安装 .NET。ARM64：

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
```

## 操作

| 操作 | 效果 |
| --- | --- |
| 鼠标移入胶囊 | 放大展开 |
| 鼠标移开 | 收起（未固定时） |
| 左键单击 | 固定 / 取消固定 |
| 右键单击 | 打开菜单 |
| 托盘图标双击 | 显示灵动岛 |
| 托盘图标右键 | 显示、设置、演示通知、开机启动、退出 |

命令行参数：

```text
--demo         启动后展开并固定，方便截图 / 调试
--settings     启动后直接打开设置窗口
--autostart    由开机启动项使用；重复实例时静默退出
```

## 开发命令

```powershell
.\scripts\build.ps1                 # Debug 构建
.\scripts\build.ps1 -Configuration Release
.\scripts\test.ps1                  # 单元测试
.\scripts\run.ps1 -Demo             # 构建 + 运行
.\scripts\publish.ps1               # 自包含单文件发布
```

全部脚本都兼容 Windows PowerShell 5.1。若系统执行策略限制 `.ps1`，统一用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## 项目结构

```text
dynamic-island/
├─ src/
│  ├─ DynamicIsland.Core/           # 无 UI 依赖：配置、尺寸、定位
│  │  ├─ Layout/IslandPlacement.cs
│  │  ├─ Models/IslandMetrics.cs
│  │  ├─ Models/IslandSettings.cs
│  │  └─ Services/SettingsStore.cs
│  └─ DynamicIsland.App/            # WPF 宿主
│     ├─ Interop/NativeMethods.cs
│     ├─ Services/                  # 托盘、开机启动、日志
│     ├─ ViewModels/
│     ├─ Views/IslandWindow.*
│     ├─ Views/SettingsWindow.*
│     └─ Themes/Theme.xaml
├─ tests/DynamicIsland.Core.Tests/
├─ scripts/                         # bootstrap / build / test / run / publish
├─ docs/                            # 架构、开发说明、路线图
├─ .vscode/                         # 构建任务、调试配置
└─ DynamicIsland.sln
```

## 配置与日志

| 内容 | 路径 |
| --- | --- |
| 设置 | `%LOCALAPPDATA%\DynamicIsland\settings.json` |
| 日志 | `%LOCALAPPDATA%\DynamicIsland\logs\app.log` |
| 开机启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DynamicIsland` |

设置由 `%LOCALAPPDATA%` 保存，不联网、不上传。损坏的配置会回退到默认值，不会阻止启动。

## 当前限制

- 多显示器：Core 已预留 `PreferredScreen`，当前设置页未开放显示器选择，默认使用主屏。
- 通知目前是内置演示通道，还没有接入 Windows 通知监听。
- 尚未接入媒体控制、天气、日程等真实数据源。
- 未提供 MSIX / Inno Setup 安装器和代码签名；当前通过发布目录或 zip 运行。
- 自包含单文件首次启动会把原生库解压到临时目录，这是 .NET 单文件在 WPF 下的正常行为。

后续计划见 [docs/roadmap.md](docs/roadmap.md)。

## 文档

- [开发环境与工作流](docs/development.md)
- [架构与扩展点](docs/architecture.md)
- [路线图](docs/roadmap.md)

## 环境实测

- Windows 11 `10.0.26200`
- .NET SDK `10.0.403`（用户级安装，无管理员权限）
- Visual Studio Code `1.143.1` + C# Dev Kit / C# 扩展
- MinGit `2.56.0`（可选，用于版本控制）
- `dotnet build` 0 警告 0 错误；`dotnet test` 10/10 通过
