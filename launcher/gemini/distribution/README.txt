Gemini 猫娘启动选择器 · v1.3.0

1. 完整解压 ZIP。不要直接在压缩包里运行。
2. 确认已经安装 Gemini 桌面版与 Antigravity，桌面有 Gemini 快捷方式，开始菜单有 Antigravity 快捷方式。
3. 双击 Install-Gemini-Launcher.cmd。安装时不会打开 AI 客户端。
4. 双击桌面 Gemini，选择“Gemini 桌面版”或“Antigravity”。Esc 可取消。

程序已编译好，无需 .NET SDK。安装脚本需要 PowerShell 7；运行程序需要 Windows 自带的 .NET Framework 4.7.2 或更高版本。
PowerShell 官方安装说明：https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows

安装后，程序位于 %LOCALAPPDATA%\AI-Mascot-Desktop\GeminiLauncher\<时间戳>。
原快捷方式和 Restore.ps1 位于 %LOCALAPPDATA%\AI-Mascot-Desktop\GeminiLauncher-Backups\<时间戳>。
安装窗口会输出具体备份路径。需要还原时，在 PowerShell 7 中运行该备份目录的 Restore.ps1。

只更新指定的桌面 Gemini 入口，不修改 Gemini Notebook、开始菜单或客户端程序。
选择器不会自动选择应用；不连接模型、不读取聊天、不发送消息。被选中客户端的网络行为由客户端决定。
不要直接运行 app\GeminiLauncher.exe：它需要安装器根据本机快捷方式生成启动配置。

自定义安装目录或快捷方式位置：
在 PowerShell 7 中运行以下命令，并替换相应路径：
./scripts/Install.ps1 -PayloadDirectory ./app -DesktopShortcut "<Gemini 快捷方式完整路径>" -AntigravityShortcut "<Antigravity 快捷方式完整路径>"
其他参数：-InstallRoot、-BackupRoot。无需管理员权限。

代码遵循 LICENSE；猫娘 PNG/ICO 素材遵循 ASSETS.md，不包含在 MIT 代码许可内。
验证范围见 VALIDATION.md。真实鼠标点击、桌面截图和干净虚拟机验收尚未完成。
