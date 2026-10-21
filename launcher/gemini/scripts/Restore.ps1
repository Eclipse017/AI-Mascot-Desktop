#requires -Version 7.0
[CmdletBinding()]
param([string]$BackupDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$record = Get-Content -LiteralPath (Join-Path $BackupDirectory 'installation.json') -Raw | ConvertFrom-Json
$original = Join-Path $BackupDirectory 'Gemini.original.lnk'
if ((Get-FileHash -LiteralPath $original).Hash -ne $record.OriginalSHA256) { throw 'Original backup integrity check failed.' }
$currentHash = (Get-FileHash -LiteralPath $record.DesktopShortcut).Hash
if ($currentHash -eq $record.OriginalSHA256) { Write-Output 'PASS: original shortcut already restored.'; return }
if ($currentHash -ne $record.InstalledShortcutSHA256) { throw 'Shortcut changed after installation. Refusing to overwrite a newer change.' }
$temporary = Join-Path (Split-Path $record.DesktopShortcut -Parent) ('.Gemini-restore-' + [guid]::NewGuid().ToString('N') + '.lnk')
Copy-Item -LiteralPath $original -Destination $temporary
[IO.File]::Replace($temporary,$record.DesktopShortcut,[NullString]::Value)
if ((Get-FileHash -LiteralPath $record.DesktopShortcut).Hash -ne $record.OriginalSHA256) { throw 'Restore readback failed.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GeminiRestoreRefresh {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern void SHChangeNotify(uint id, uint flags, string path, IntPtr other);
}
'@
[GeminiRestoreRefresh]::SHChangeNotify(0x2000,0x2005,$record.DesktopShortcut,[IntPtr]::Zero)
Write-Output 'PASS: original Gemini shortcut restored byte-for-byte. Installation and backups retained.'
