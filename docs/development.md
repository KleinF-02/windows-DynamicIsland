# 开发环境与工作流

## 1. 本机已安装的工具

本次已经为本项目准备好以下工具，全部为当前用户级安装，不依赖管理员权限：

| 工具 | 版本 | 位置 / 说明 |
| --- | --- | --- |
| .NET SDK | `10.0.403` | `%LOCALAPPDATA%\Microsoft\dotnet` |
| .NET Runtime | `10.0.14` | 同上，含 `Microsoft.WindowsDesktop.App` |
| Visual Studio Code | `1.143.1` | 用户级安装 |
| C# / C# Dev Kit | `2.160.4` / `3.40.215` | VS Code 扩展，用于 IntelliSense 和调试 |
| MinGit | `2.56.0` | `%LOCALAPPDATA%\Programs\MinGit\cmd`，可选版本控制 |
| Outlook 桌面版 | 未安装 | 桌面 COM 回退需在安装并登录 x64 Outlook 的机器上验收 |
| Outlook 网页日历 | 本地 HTTP ICS | 已用示例 ICS 验证获取与解析；真实 Microsoft 365 发布链接需用实际账号联调 |

环境脚本会自动处理以下变量：

- `PATH`：加入 `%LOCALAPPDATA%\Microsoft\dotnet` 和 `%LOCALAPPDATA%\Programs\MinGit\cmd`
- `DOTNET_ROOT`：指向用户级 .NET 根目录，确保直接启动 `DynamicIsland.exe` 时 apphost 能找到运行时

> 用户级 .NET SDK 不会写入 `C:\Program Files\dotnet`。如果直接双击 build 输出的 exe，必须已设置 `DOTNET_ROOT`；使用 `scripts/run.ps1` 或发布后的自包含版本不会遇到这个问题。

## 2. 一条命令初始化

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
```

执行内容：

1. 定位用户级 .NET 10 SDK；不存在则下载官方 `dotnet-install.ps1` 并安装到 `%LOCALAPPDATA%\Microsoft\dotnet`。
2. 写入用户环境变量 `PATH`、`DOTNET_ROOT`。
3. `dotnet restore`。
4. `dotnet build DynamicIsland.sln -c Debug`。
5. `dotnet test DynamicIsland.sln -c Debug --no-build`。

跳过构建与测试：

```powershell
.\scripts\bootstrap.ps1 -SkipBuild
```

## 3. 日常命令

```powershell
# 构建
.\scripts\build.ps1
.\scripts\build.ps1 -Configuration Release

# 测试
.\scripts\test.ps1

# 运行
.\scripts\run.ps1
.\scripts\run.ps1 -Demo
.\scripts\run.ps1 -Settings

# 不重新构建直接运行
.\scripts\run.ps1 -NoBuild

# 发布
.\scripts\publish.ps1
.\scripts\publish.ps1 -Runtime win-arm64
.\scripts\publish.ps1 -FrameworkDependent
```

如果 PowerShell 执行策略禁止运行脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Demo
```

## 4. VS Code 调试

打开项目：

```powershell
code .
```

已提供：

- `.vscode/tasks.json`：`build`、`test`、`run demo`、`publish`
- `.vscode/launch.json`：`灵动岛 (Debug + demo)`，F5 启动
- `.vscode/extensions.json`：推荐 C# Dev Kit / C# / EditorConfig

如果 F5 报找不到 .NET，先运行一次 `bootstrap.ps1`，或在 launch.json 的 `env.DOTNET_ROOT` 中确认路径。

## 5. 发布说明

默认发布命令会生成：

```text
artifacts/
└─ publish/
   ├─ DynamicIsland-0.1.0-win-x64/
   │  └─ DynamicIsland.exe
   └─ DynamicIsland-0.1.0-win-x64.zip
```

参数：

- `-Runtime win-x64`（默认）或 `win-arm64`
- `-FrameworkDependent`：输出较小，但目标机必须安装 .NET 10 Desktop Runtime
- `-NoZip`：只生成目录，不压缩

发布版内嵌了应用图标，启动不依赖本机 SDK。

## 6. 版本控制

仓库已用 MinGit 初始化在 `main` 分支，并创建了初始提交。当前提交身份是项目内的占位配置：

```powershell
git config user.name "DynamicIsland Dev"
git config user.email "dev@dynamic-island.local"
```

改成自己的身份：

```powershell
git config user.name "你的名字"
git config user.email "you@example.com"
```

MinGit 的 `git.exe` 位于 `%LOCALAPPDATA%\Programs\MinGit\cmd`，已加入用户 `PATH`。只使用 `git.exe` 即可，不需要 Git Bash。

## 7. 测试

```powershell
dotnet test DynamicIsland.sln
```

当前覆盖：

- 胶囊尺寸和阴影内边距计算
- 通知态高度小于展开态
- 顶部居中定位和边距约束
- 设置 JSON 保存 / 读取
- 损坏配置回退
- 配置值归一化（透明度、顶部距离）
- `Clone` 独立性

后续可以补充：窗口状态机测试、UI Automation 冒烟测试、发布产物启动测试。

### 常用软件手工验收

1. `.\scripts\run.ps1 -Demo`
2. 从桌面或开始菜单拖一个 `.lnk`（或 `.exe`、`.url`、`.appref-ms`）到第一个空格，确认图标和名称出现。
3. 悬停该格子，确认格子放大、黑胶唱片滑出并旋转。
4. 单击格子，确认目标软件被打开。
5. 悬停后点右上角 `×`，确认该格恢复为空；重启应用确认已填的快捷方式仍在。
6. 拖到已有格子，确认旧快捷方式被替换。

### 待办事项手工验收

1. 展开灵动岛，点第三个圆点进入“待办事项”。
2. 在顶部输入框输入文字，按回车或点 `+`，确认待办出现。
3. 直接点击待办文字修改，点其他位置，确认修改保存；重启应用确认仍在。
4. 点待办左侧圆圈，确认出现粒子扩散和整行淡出，约 0.3 秒后待办消失。
5. 检查 `%LOCALAPPDATA%\DynamicIsland\todos.json`，确认已完成的条目被删除。

### Calendar 手工验收

**Microsoft 365 网页版（推荐）**

1. 在 Outlook 网页版发布日历：设置 → 日历 → 共享日历 → 发布日历，复制 ICS 链接。
2. 打开灵动岛设置 → 日历，粘贴 ICS 链接并保存。
3. 展开灵动岛，点底部的第二个圆点，确认显示今天的“时间 · 主题 · 地点”。
4. 在示例 ICS 里放一条重复日程和一条全天日程，确认重复实例被展开、全天显示“全天”。
5. 把链接改成无效链接后刷新，确认出现可读错误而不是崩溃。

离线验证可以启动一个本地 HTTP 服务，把 ICS 文件放在目录里：

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory .\artifacts\ics-test
```

然后把 `http://127.0.0.1:8765/calendar.ics` 填入设置。

**桌面版 Outlook 回退（可选）**

1. 清空网页日历链接，确认本机安装了 x64 桌面版 Outlook 并已登录。
2. 切换日历页并刷新，确认读取默认日历。
3. 不安装 Outlook 时，确认显示“未配置日历来源”。

本机（开发环境）没有桌面版 Outlook；已用本地 HTTP ICS 源完成网页日历的获取、重复日程、全天日程、已取消事件过滤和 UI 验收。

## 8. 常见问题

### `dotnet` 不是可识别的命令

新开一个终端。若仍不存在：

```powershell
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"
dotnet --version
```

### 双击 exe 弹出 “You must install .NET”

用户级运行时需要 `DOTNET_ROOT`：

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:Path = "$env:DOTNET_ROOT;$env:Path"
```

或者使用发布脚本生成 self-contained 版本。

### 构建提示文件被占用

先退出正在运行的灵动岛：

```powershell
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process
```

### 启动后没有窗口

1. 确认系统托盘中有“灵动岛”图标。
2. 双击托盘图标或运行 `.\scripts\run.ps1 -Demo`。
3. 查看 `%LOCALAPPDATA%\DynamicIsland\logs\app.log`。
4. 确认没有残留进程：`Get-Process DynamicIsland`。

### 日历显示“未配置日历来源”

这是网页链接为空、且本机没有桌面版 Outlook 时的正常提示。

- 使用 Microsoft 365 网页版：在设置 → 日历粘贴 Outlook 发布的 ICS 链接。
- 使用桌面版 Outlook：安装并登录 x64 Outlook，或在设置里清空 ICS 链接后回退 COM。
- 检查 `Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\Outlook.Application\CLSID'` 是否存在，以及 Office 位数。

### 日历链接刷新失败

1. 在浏览器打开 ICS 链接，确认下载的是 `.ics` 而不是登录页/错误页。
2. 重新在 Outlook 网页版执行“发布日历”，旧链接可能已失效。
3. 检查组织是否禁用了“发布日历”；若禁用，需要改用 Microsoft Graph + OAuth。
4. 本地开发可用 `http.server` + 示例 `.ics` 排除网络问题。

### 设置改了但没生效

设置页点击“保存设置”后才写入。保存后立即应用到当前窗口；`TopMargin`、`AlwaysOnTop`、`Opacity` 会即时更新。

### 开机启动没有执行

检查：

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name DynamicIsland
```

开发阶段该值会指向 `bin\Debug\net10.0-windows\DynamicIsland.exe`。正式使用前请先发布，再从发布目录启动并重新开启开机启动，让注册表指向稳定的安装路径。

## 9. 代码约定

- 命名空间：`DynamicIsland.App` / `DynamicIsland.Core`
- Core 不引用 WPF 或 WinForms，便于测试和未来复用
- XAML 样式集中在 `Themes/Theme.xaml`
- 新颜色、字体、按钮样式优先放主题资源，不在页面里硬编码
- 新增配置项：`IslandSettings` → `Normalize()` → `SettingsViewModel` → `SettingsWindow`
- 新增通知：调用 `IslandWindow.ShowNotification(title, message, duration)`
- 提交前：`build + test + 手动运行 -Demo`
