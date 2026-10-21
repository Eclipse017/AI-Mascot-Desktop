#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AI-Mascot-Desktop\GeminiLauncher'),
    [string]$BackupRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AI-Mascot-Desktop\GeminiLauncher-Backups'),
    [string]$PayloadDirectory,
    [string]$DesktopShortcut = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Gemini.lnk'),
    [string]$AntigravityShortcut = (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Antigravity.lnk')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $root 'artifacts\build' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$install = Join-Path $InstallRoot $stamp
$backup = Join-Path $BackupRoot $stamp
$shell = New-Object -ComObject WScript.Shell
function Read-Link([string]$path) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Shortcut missing: $path" }
    $link = $shell.CreateShortcut($path)
    [pscustomobject]@{ Path=$path; Target=$link.TargetPath; Arguments=$link.Arguments; WorkingDirectory=$link.WorkingDirectory; Icon=$link.IconLocation; Description=$link.Description; Hotkey=$link.Hotkey; WindowStyle=$link.WindowStyle; SHA256=(Get-FileHash -LiteralPath $path).Hash }
}
$original = Read-Link $DesktopShortcut
$agent = Read-Link $AntigravityShortcut
$geminiSource = $DesktopShortcut
if ([IO.Path]::GetFileName($original.Target) -eq 'GeminiLauncher.exe') {
    $geminiSource = Join-Path (Split-Path $original.Target -Parent) 'targets\gemini.lnk'
}
$gemini = Read-Link $geminiSource
foreach ($item in @($gemini,$agent)) {
    if (!(Test-Path -LiteralPath $item.Target -PathType Leaf) -or [IO.Path]::GetExtension($item.Target) -ne '.exe') { throw "Application target missing: $($item.Target)" }
    if ([IO.Path]::GetFileName($item.Target) -eq 'GeminiLauncher.exe') { throw 'Recursive launch target rejected.' }
}
foreach ($name in @('GeminiLauncher.exe','GeminiLauncher.exe.config','Gemini.ico')) {
    if (!(Test-Path -LiteralPath (Join-Path $build $name))) { throw "Build first: missing $name" }
}
if ((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $backup)) { throw 'Installation timestamp already exists.' }
New-Item -ItemType Directory -Path (Join-Path $install 'targets'),$backup -Force | Out-Null
Copy-Item -LiteralPath $DesktopShortcut -Destination (Join-Path $backup 'Gemini.original.lnk')
if ((Get-FileHash -LiteralPath (Join-Path $backup 'Gemini.original.lnk')).Hash -ne $original.SHA256) { throw 'Original backup verification failed.' }
$original | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'original.json') -Encoding utf8
foreach ($name in @('GeminiLauncher.exe','GeminiLauncher.exe.config','Gemini.ico')) {
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination $install
    if ((Get-FileHash -LiteralPath (Join-Path $build $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $install $name)).Hash) { throw "Package verification failed: $name" }
}
Copy-Item -LiteralPath $geminiSource -Destination (Join-Path $install 'targets\gemini.lnk')
Copy-Item -LiteralPath $AntigravityShortcut -Destination (Join-Path $install 'targets\antigravity.lnk')
if ((Get-FileHash -LiteralPath (Join-Path $install 'targets\gemini.lnk')).Hash -ne $gemini.SHA256 -or (Get-FileHash -LiteralPath (Join-Path $install 'targets\antigravity.lnk')).Hash -ne $agent.SHA256) { throw 'Launch target copy verification failed.' }
$document = [xml]'<targets />'
foreach ($entry in @(@('gemini',$gemini.Target),@('antigravity',$agent.Target))) {
    $element = $document.CreateElement('app')
    $element.SetAttribute('id', $entry[0]); $element.SetAttribute('shortcut', $entry[0] + '.lnk'); $element.SetAttribute('executable', $entry[1])
    [void]$document.DocumentElement.AppendChild($element)
}
$document.Save((Join-Path $install 'targets.xml'))
$pending = Join-Path $backup 'Gemini.new.lnk'
$link = $shell.CreateShortcut($pending)
$link.TargetPath = Join-Path $install 'GeminiLauncher.exe'
$link.WorkingDirectory = $install
$link.IconLocation = (Join-Path $install 'Gemini.ico') + ',0'
$link.Description = '选择打开 Gemini 桌面版或 Antigravity'
$link.Hotkey = $original.Hotkey
$link.WindowStyle = 1
$link.Save()
$expected = Read-Link $pending
if ($expected.Target -ne (Join-Path $install 'GeminiLauncher.exe') -or $expected.Arguments -ne '' -or $expected.Icon -ne ((Join-Path $install 'Gemini.ico') + ',0')) { throw 'Prepared shortcut readback failed.' }
Copy-Item -LiteralPath "$PSScriptRoot\Restore.ps1" -Destination $backup
$record = [ordered]@{ DesktopShortcut=$DesktopShortcut; InstallDirectory=$install; OriginalSHA256=$original.SHA256; InstalledShortcutSHA256=$expected.SHA256; Gemini=$gemini; Antigravity=$agent; InstalledAt=(Get-Date -Format o) }
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'installation.json') -Encoding utf8
if ((Get-FileHash -LiteralPath $DesktopShortcut).Hash -ne $original.SHA256) { throw 'Desktop shortcut changed during installation; stopping before replacement.' }
# Replace from the same directory/volume, keeping both the original and replacement backups.
$temporary = Join-Path (Split-Path $DesktopShortcut -Parent) ('.Gemini-install-' + [guid]::NewGuid().ToString('N') + '.lnk')
Copy-Item -LiteralPath $pending -Destination $temporary
[IO.File]::Replace($temporary, $DesktopShortcut, [NullString]::Value)
$installed = Read-Link $DesktopShortcut
if ($installed.SHA256 -ne $expected.SHA256) { throw 'Installed desktop shortcut verification failed. Restore script is in the backup directory.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GeminiShellRefresh {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern void SHChangeNotify(uint id, uint flags, string path, IntPtr other);
}
'@
[GeminiShellRefresh]::SHChangeNotify(0x2000,0x2005,$DesktopShortcut,[IntPtr]::Zero)
[GeminiShellRefresh]::SHChangeNotify(0x8000000,0,[string]$null,[IntPtr]::Zero)
[pscustomobject]@{Status='PASS';DesktopShortcut=$DesktopShortcut;Installed=$installed;BackupDirectory=$backup;RestoreScript=(Join-Path $backup 'Restore.ps1')} | ConvertTo-Json -Depth 5
