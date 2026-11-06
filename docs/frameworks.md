# 使用的框架与技术选型

## 总览

| 领域 | 技术 | 用途 |
| --- | --- | --- |
| 运行时 | **.NET 10** | 应用运行时和工具链 |
| 语言 | **C#**（启用 nullable） | 全部业务和 UI 代码 |
| 桌面 UI | **WPF** | 透明胶囊、动画、数据绑定、设置页 |
| Windows 互操作 | **Win32 P/Invoke** | 置顶、窗口样式、Shell 图标、拖拽、开始菜单检测 |
| 托盘 / 屏幕 | **Windows Forms**（仅 `NotifyIcon` / `Screen`） | 系统托盘、工作区定位 |
| 日历 | **Ical.Net 4.3.1** | RFC 5545 ICS 解析与重复日程展开 |
| 数据持久化 | **System.Text.Json** | 设置、快捷方式、待办原子写入 |
| 测试 | **xUnit** | Core 层单元测试 |
| 脚本 | **PowerShell 7** | 构建、测试、发布、版本发布 |
| CI/CD | **GitHub Actions** | Windows 构建、测试、标签发布 |
| 本地模型 | **Ollama / OpenAI-compatible HTTP** | 翻译与 AI Agent |

## 为什么选 WPF

- 透明分层窗口、逐像素尺寸动画和 DropShadow 都可控。
- 运行在 Windows 11 自带 .NET 桌面运行时上，自包含发布后目标机无需安装 .NET。
- 与 Win32、Shell、WinForms 托盘互操作成熟。
- 单窗口、多状态的产品不需要引入浏览器运行时。

没有选择 Electron：包体大、内存高、透明置顶/窗口动画需要额外原生桥接。没有选择 WinUI 3：当前项目更看重窗口尺寸逐帧控制和单文件分发，WPF 的实现风险更低。

## 窗口与系统集成

- `AllowsTransparency=True` 的分层窗口 + `WS_EX_TOOLWINDOW`。
- 使用 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 保证胶囊在开始菜单等系统 UI 前仍然可见。
- 通过 `GetForegroundWindow` + 进程名识别 `StartMenuExperienceHost` / `SearchHost` / `ShellExperienceHost`。
- `SHGetFileInfo` 提取 `.lnk` / `.exe` / `.html` 图标；`DestroyIcon` 释放句柄。
- `SHParseDisplayName` / `ILCombine` / `SHGetPathFromIDListW` 解析 Shell IDList。
- 应用执行别名使用 `FSCTL_GET_REPARSE_POINT` 解析 AppExecLink，读取真实包内 exe 图标。
- 开机启动只写当前用户 `HKCU\...\Run`。

## 日历

- 首选 Microsoft 365 网页版“发布日历”的只读 ICS，不需要 Azure 应用注册。
- 支持 `http(s)` 和本地 `.ics` 文件。
- 使用 `Ical.Net` 的 `GetOccurrences(today, tomorrow)` 展开重复日程。
- 未配置 ICS 时回退桌面 Outlook COM；COM 调用放在专用 STA 线程，不阻塞 UI。
- 日历链接含私人令牌，只写本机设置，不写日志。

## 本地模型

翻译和 AI Agent 共用 `LocalTranslationService.CompleteAsync`：

- 默认 `http://127.0.0.1:11434`，优先尝试 Ollama `/api/chat`。
- 其他地址优先使用 OpenAI 兼容 `/v1/chat/completions`（LM Studio、llama.cpp 等）。
- 模型名留空时读取 `/api/tags` 或 `/v1/models` 的第一个模型。
- 请求 90 秒超时，不启用流式输出；支持取消。
- 只请求设置中的本机地址，不接入云厂商 SDK。

## 数据与测试

- `SettingsStore` / `ShortcutStore` / `TodoStore` 使用临时文件 + 原子替换，避免写坏 JSON。
- Core 层测试覆盖状态、尺寸、四边定位、序列化和小屏自适应用例，当前总计 17 个测试。
- GitHub Actions 在 `windows-latest` 上 restore/build/test，并在 tag 上构建自包含 zip。

## 依赖许可

运行时第三方依赖只有：

- `Ical.Net` 4.3.1 — MIT
- `NodaTime` 3.2.0 — Apache-2.0

详见 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)。

## 目标环境

- Windows 11 x64 / arm64
- 自包含发布：目标机不需要预装 .NET
- 本地翻译/Agent：需要本机运行 Ollama 或 OpenAI-compatible 服务，并加载模型
- 日历：Microsoft 365 网页版 ICS，或桌面版 Outlook COM
