# 版本管理与发布

## 版本号

项目使用 **Semantic Versioning 2.0.0**：

```text
MAJOR.MINOR.PATCH
```

- `MAJOR`：不兼容的交互/数据格式变更；
- `MINOR`：向下兼容的新功能区或明显能力；
- `PATCH`：修复、文案和性能改进。

当前版本在根目录的 [`VERSION`](../VERSION) 中维护：

```text
0.6.0
```

`Directory.Build.props` 在构建时读取这个文件，程序集版本和发布包名称都从同一处派生，避免多处手改。

**持续版本策略：** 每一次用户可见迭代都必须递增 `VERSION` 并创建新的 annotated tag；旧 tag 和已经发布的 Release 不移动、不覆盖、不删除。修复走 `PATCH`，新功能区或明显能力走 `MINOR`。

## 版本历史

Git 提交完整保留了每一次开发过程；下面的 annotated tag 标记了关键可回看检查点：

| 标签 | 内容 | 提交 |
| --- | --- | --- |
| `v0.1.0-dev.1` | 初始 WPF 胶囊骨架 | `b1f9566` |
| `v0.1.0-dev.2` | 贴顶、悬停放大、登录名 | `1d7bcad` |
| `v0.1.0-dev.3` | Calendar 与 Microsoft 365 ICS | `059fe5d` |
| `v0.1.0-dev.4` | 待办与粒子完成动画 | `637ce2d` |
| `v0.1.0-dev.5` | 常用软件快捷方式 | `c50511e` |
| `v0.1.0-dev.6` | 深色 / 浅色双主题 | `2cde7cd` |
| `v0.1.0-dev.7` | 本地模型翻译 | `d5a6b1d` |
| `v0.1.0-dev.8` | AI Agent 工作区 | `a964235` |
| `v0.1.0` | 首个公开版本（含文档、版本流程） | 发布提交 |
| `v0.2.0` | AI Agent 与翻译统一悬停窗口策略；版本流程固化 | 发布提交 |
| `v0.2.1` | 修复 Agent 中文输入时窗口收起 | 发布提交 |
| `v0.3.0` | 五个功能区停留策略审计、分辨率自适应、窗口动画优化 | 发布提交 |
| `v0.4.0` | 动态模糊、渐隐过渡与 60fps 动画 | 发布提交 |
| `v0.5.0` | 弹簧缓动、连续圆角形变、内容 mask 揭示 | 发布提交 |
| `v0.5.1` | 修复 Agent 空输入时鼠标移出不收起 | 发布提交 |
| `v0.6.0` | Agent 文件上传与本地文本/.docx 解析 | 发布提交 |

`v0.1.0-dev.*` 是**开发检查点**，不是对外发布二进制。GitHub 会对 `v0.1.0` 及之后的正式标签自动创建 Release；当前最新版本是 `v0.6.0`。

查看历史版本：

```powershell
git tag --list
git show v0.1.0-dev.5
git checkout v0.1.0-dev.5
```

## 发布新版本

1. 修改 `VERSION`（例如下一版 `0.6.1`）。
2. 更新 `CHANGELOG.md`，把 `Unreleased` 移到新版本号下。
3. 提交所有改动：

   ```powershell
   git add .
   git commit -m "chore(release): v0.6.1"
   ```

4. 构建、测试并打 annotated tag：

   ```powershell
   .\scripts\release.ps1
   ```

5. 推送主分支和 tag：

   ```powershell
   git push origin main
   git push origin v0.6.1
   ```

6. GitHub Actions 的 `release` workflow 会：
   - 在 `windows-latest` 上 restore / build / test；
   - 构建自包含 `win-x64` 单文件；
   - 校验 tag 与 `VERSION` 一致；
   - 上传 `DynamicIsland-<version>-win-x64.zip` 并生成 Release Notes。

如果版本号是预发布（例如 `0.2.0-beta.1`），workflow 会把 Release 标记为 prerelease。

## 发布产物

| 产物 | 路径 |
| --- | --- |
| 自包含目录 | `artifacts/publish/DynamicIsland-<version>-win-x64/` |
| zip | `artifacts/publish/DynamicIsland-<version>-win-x64.zip` |
| 单文件 exe | `artifacts/publish/DynamicIsland-<version>-win-x64/DynamicIsland.exe` |

`artifacts/` 已被 `.gitignore` 忽略；发布二进制只通过 GitHub Releases 分发，不提交到源码仓库。

## GitHub 仓库

当前仓库已发布到：

<https://github.com/KleinF-02/windows-DynamicIsland>

发布页：

<https://github.com/KleinF-02/windows-DynamicIsland/releases>

首次上传时保留了 GitHub 自动生成的初始化提交，并通过 merge commit 合并到本地历史；没有使用强推。remote 配置：

```powershell
git remote add origin https://github.com/KleinF-02/windows-DynamicIsland.git
git push -u origin main
git push origin --tags
```

如果使用 SSH：

```powershell
git remote set-url origin git@github.com:KleinF-02/windows-DynamicIsland.git
```

> 不要把 `%LOCALAPPDATA%\DynamicIsland` 下的设置、日历链接、快捷方式或个人日志提交到仓库；它们不在 Git 工作树中。

## 分支与提交约定

- 主分支：`main`
- 提交信息建议使用 Conventional Commits：
  - `feat(calendar): ...`
  - `fix(shortcuts): ...`
  - `docs: ...`
  - `chore(release): ...`
- 一个 PR 尽量只做一个功能区或一个修复。
- 发布必须经过测试；紧急修复也先提交 `PATCH` 版本。

## 历史版本为什么用 tag，而不是复制目录

- Git tag 指向不可变的 commit，能精确恢复源码；
- 不需要在仓库里堆积多份构建输出；
- GitHub Actions 可以从 tag 重建发布包；
- `CHANGELOG.md` 记录每个版本的用户可见变化。

如果确实需要保留旧版二进制，请把 zip 附加到对应 GitHub Release，而不是提交进 Git。
