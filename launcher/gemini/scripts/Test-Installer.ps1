#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $root ('artifacts\installer tests\中文 ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$desktop = Join-Path $fixture 'Gemini.lnk'
$agent = Join-Path $fixture 'Antigravity.lnk'
$inertTarget = Join-Path ([Environment]::SystemDirectory) 'notepad.exe'
foreach ($path in @($desktop, $agent)) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $inertTarget
    $link.Arguments = '--fixture-only "中文 argument"'
    $link.WorkingDirectory = $fixture
    $link.Description = 'Inert installer fixture; never executed'
    $link.Save()
}
$originalHash = (Get-FileHash -LiteralPath $desktop).Hash
$agentHash = (Get-FileHash -LiteralPath $agent).Hash
$checks = 0
function Assert-Check([bool]$condition, [string]$name) {
    if (!$condition) { throw $name }
    $script:checks++
}
$options = @{ DesktopShortcut=$desktop; AntigravityShortcut=$agent; InstallRoot=(Join-Path $fixture 'installed'); BackupRoot=(Join-Path $fixture 'backups') }
$first = & "$PSScriptRoot\Install.ps1" @options | ConvertFrom-Json
Assert-Check ($first.Status -eq 'PASS') 'Installation readback'
$firstRoot = $first.Installed.WorkingDirectory
Assert-Check ((Get-FileHash -LiteralPath "$firstRoot\targets\gemini.lnk").Hash -eq $originalHash) 'Gemini original entry preserved'
Assert-Check ((Get-FileHash -LiteralPath "$firstRoot\targets\antigravity.lnk").Hash -eq $agentHash) 'Antigravity original entry preserved'
Assert-Check ((Get-FileHash -LiteralPath $agent).Hash -eq $agentHash) 'Source Antigravity shortcut unchanged'
$firstHash = (Get-FileHash -LiteralPath $desktop).Hash
$second = & "$PSScriptRoot\Install.ps1" @options | ConvertFrom-Json
Assert-Check ((Get-FileHash -LiteralPath (Join-Path $second.Installed.WorkingDirectory 'targets\gemini.lnk')).Hash -eq $originalHash) 'Reinstallation cannot recursively launch the picker'
& $second.RestoreScript | Out-Null
Assert-Check ((Get-FileHash -LiteralPath $desktop).Hash -eq $firstHash) 'Restore previous installation'
& $first.RestoreScript | Out-Null
Assert-Check ((Get-FileHash -LiteralPath $desktop).Hash -eq $originalHash) 'Exact original restore'
& $first.RestoreScript | Out-Null
Assert-Check ((Get-FileHash -LiteralPath $desktop).Hash -eq $originalHash) 'Idempotent restore'
Copy-Item -LiteralPath (Join-Path $first.BackupDirectory 'Gemini.new.lnk') -Destination $desktop -Force
$link = $shell.CreateShortcut($desktop); $link.Description = 'Later user change'; $link.Save()
$laterHash = (Get-FileHash -LiteralPath $desktop).Hash
$refused = $false
try { & $first.RestoreScript | Out-Null } catch { $refused = $true }
Assert-Check $refused 'Reject overwriting a later change'
Assert-Check ((Get-FileHash -LiteralPath $desktop).Hash -eq $laterHash) 'Later user change preserved'
[pscustomobject]@{Status='PASS';Checks=$checks;AIClientsStarted=$false;Fixture=$fixture} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts\installer-verification.json') -Encoding utf8
Write-Output "PASS: $checks installer checks. Fixture shortcuts only; no application launched."
