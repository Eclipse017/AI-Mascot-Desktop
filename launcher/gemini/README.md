# Gemini 猫娘启动选择器

将桌面 **Gemini** 快捷方式换成紫发猫娘图标。双击后选择 **Gemini 桌面版** 或 **Antigravity**，选中后打开对应的已安装应用并关闭选择窗口。Esc 或关闭窗口直接取消；没有默认自动启动、登录启动或后台驻留。

这是独立可选组件，不改变五角色桌宠的跟随逻辑。它只在用户选择后启动对应客户端；选择器本身不连接模型、不读聊天、不发送消息。客户端自身的网络行为由客户端决定。

## 下载即用

从 [v1.3.0 Release](https://github.com/Eclipse017/AI-Mascot-Desktop/releases/tag/v1.3.0) 下载完整桌宠包或 `Gemini-Launcher-v1.3.0-win-x64.zip`。完整解压后，在启动器目录双击 `Install-Gemini-Launcher.cmd`。程序已编译好，无需 .NET SDK；安装需要 PowerShell 7，运行需要 .NET Framework 4.7.2+。先准备可用的桌面 Gemini 快捷方式与开始菜单 Antigravity 快捷方式。

完整包的启动器在 `Gemini-Launcher/` 中；独立包的解压目录就是启动器目录。随包 `README.txt` 说明安装、备份、恢复及自定义路径。不要直接运行未配置的 `app/GeminiLauncher.exe`。

## 构建

需要 Windows、PowerShell 7，以及 .NET Framework 4.7.2 或更高版本。使用 Windows 的 .NET Framework C# 编译器，不依赖桌宠使用的 .NET 10 SDK。

在仓库根目录运行：

```powershell
pwsh -NoProfile -File ./launcher/gemini/scripts/Build.ps1 -Test
pwsh -NoProfile -File ./launcher/gemini/scripts/Test-Installer.ps1
```

输出位于 `launcher/gemini/artifacts/build/`。测试使用启动替身或不会执行的快捷方式，不启动 Gemini、Antigravity 或其他 AI 客户端。

## 安装到桌面 Gemini 入口

先确认桌面存在可用的 `Gemini.lnk`，开始菜单存在可用的 `Antigravity.lnk`，然后运行：

```powershell
pwsh -NoProfile -File ./launcher/gemini/scripts/Install.ps1
```

默认将程序安装到 `%LOCALAPPDATA%\AI-Mascot-Desktop\GeminiLauncher\<时间戳>`，备份保存在相邻的 `GeminiLauncher-Backups\<时间戳>`。可通过 `-InstallRoot`、`-BackupRoot`、`-DesktopShortcut` 和 `-AntigravityShortcut` 指定其他位置；无需管理员权限。

安装器保存完整原快捷方式，再替换指定的桌面入口。Gemini Notebook、开始菜单和客户端 EXE 不会被修改。两个原始启动入口按字节复制到安装目录的 `targets/`，参数等元数据保持原样，避免递归调用桌面选择器。重装时继续使用先前保存的 Gemini 原始入口。

安装完成会输出备份目录和 `RestoreScript`。运行该恢复脚本即可恢复原快捷方式；恢复前检查哈希，若快捷方式被其他操作改过则停止，避免覆盖后续修改。恢复保留安装文件与备份。

客户端移动安装目录后，应先准备正确的应用快捷方式再重新安装；也可以同步更新已安装目录的 `targets.xml` 与对应 `targets/*.lnk`。找不到目标时窗口保留并显示错误，不改为打开网页或其他程序。

## 素材和验证

复用仓库已有的 `assets/pets/gemini/gemini.ico` 和 `assets/console/gemini.png`；素材继续遵循 [ASSETS.md](../../ASSETS.md)，不属于 MIT 代码许可。

测试结果和已知验收边界见 [VALIDATION.md](VALIDATION.md)。源码构建和 Release 下载包使用同一套程序与安装/恢复逻辑；下载包通过 `-PayloadDirectory` 指定随包预编译文件。
