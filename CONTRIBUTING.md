# Contributing

感谢你愿意改进灵动岛。

## 开发环境

- Windows 11
- .NET 10 SDK
- PowerShell 7

```powershell
.\scripts\bootstrap.ps1
dotnet test DynamicIsland.sln
.\scripts\run.ps1
```

## 提交前检查

1. `dotnet build DynamicIsland.sln -c Debug` 无警告/错误。
2. `dotnet test DynamicIsland.sln -c Debug --no-build` 全部通过。
3. 涉及 UI 的改动至少手工验证深色/浅色两种主题。
4. 不提交 `%LOCALAPPDATA%\DynamicIsland`、日志、日历链接、快捷方式或个人截图。
5. 不提交 `artifacts/`、`bin/`、`obj/`。

## 提交信息

建议使用 Conventional Commits：

```text
feat(calendar): add ...
fix(shortcuts): fix ...
docs: update ...
chore(release): v0.2.0
```

## 发布

版本号只改根目录 `VERSION`；`Directory.Build.props` 会自动读取。然后更新 `CHANGELOG.md`，运行：

```powershell
.\scripts\release.ps1
```

详见 [docs/versioning.md](docs/versioning.md)。
