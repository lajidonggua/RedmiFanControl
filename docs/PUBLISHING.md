# 发布到自己的 GitHub

这个目录本身就是独立 Git 仓库。先检查 `.gitignore`、`git status` 和待提交内容；不要把真实 `runs/`、配置、密钥、BIOS 文件或整个原研究目录加进去。

## 首次推送

1. 在 GitHub 创建名为 `RedmiFanControl`（或自选名称）的**空仓库**，不要额外生成 README/LICENSE，以免首次历史冲突。
2. 在当前仓库目录执行，替换下面的 `YOUR_NAME` 和仓库名称：

```powershell
git remote add origin https://github.com/YOUR_NAME/RedmiFanControl.git
git push -u origin main
```

由 Git / GitHub Desktop 的正常登录流程授权，不要把访问令牌粘进代码、终端参数或聊天。
也可以用 GitHub Desktop 的 Add existing repository → Publish repository。

## 首个实验发布

确认 main 的 Actions 成功后，推送与 `app/AssemblyInfo.cs` 一致的标签：

```powershell
git tag v0.9.0
git push origin v0.9.0
```

Actions 先下载并校验固定官方模块/源码，再构建和执行纯离线测试；生成 Windows x64 单 EXE 与文档 ZIP，分别隔离运行验证，最后创建实验性 Pre-release。
普通 main/PR 构建仅产生可下载 Artifact，不会创建 Release。工作流自动提供的 GITHUB_TOKEN 已分离最小读取/发布权限，不需额外配置 PAT。
仓库/组织必须允许 GitHub Actions；若组织策略禁止写 Release，需要仓库管理员允许此工作流的 contents:write。

后续发布先修改程序集版本、提交，再推送相同新版本标签。不要复用/覆盖已存在的 Release，也不要只改 tag 而不改代码版本。

## 本地验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Restore-Dependencies.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Package.ps1 -Executable dist/RedmiFanControl-0.9.0-win-x64.exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Package.ps1 -Archive dist/RedmiFanControl-0.9.0-win-x64.zip
```

本地打包通过与 GitHub 托管运行成功是两回事。首次远程运行仍需在 Actions 页面确认，尤其是仓库权限、网络和 Windows runner 环境。

下载的 EXE 可直接启动；ZIP 根目录也有同一个 EXE，不再发布 CMD/BAT 启动器。`SHA256SUMS.txt` 同时记录这两个发布文件。

模块来源与集成参考：[PawnIO 官方集成说明](https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules)。
发布接口参考：[GitHub CLI release create](https://cli.github.com/manual/gh_release_create)。
自启行为参考：[Microsoft Run/RunOnce 文档](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)。
