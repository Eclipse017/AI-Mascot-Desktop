#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.3.0',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts/releases/v$Version-" + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new release directory; existing output is preserved.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$full = Join-Path $OutputDirectory "AI-Mascot-Desktop-v$Version-win-x64"
& "$PSScriptRoot\Build.ps1" -Publish -OutputDirectory $full
& "$root\launcher\gemini\scripts\Build.ps1" -Test
& "$root\launcher\gemini\scripts\Test-Installer.ps1"
$launcher = Join-Path $full 'Gemini-Launcher'
New-Item -ItemType Directory -Path "$launcher\app","$launcher\scripts" -Force | Out-Null
foreach ($name in @('GeminiLauncher.exe','GeminiLauncher.exe.config','Gemini.ico')) {
    Copy-Item -LiteralPath "$root\launcher\gemini\artifacts\build\$name" -Destination "$launcher\app"
}
foreach ($name in @('Install.ps1','Restore.ps1')) { Copy-Item -LiteralPath "$root\launcher\gemini\scripts\$name" -Destination "$launcher\scripts" }
foreach ($name in @('Install-Gemini-Launcher.cmd','README.txt')) { Copy-Item -LiteralPath "$root\launcher\gemini\distribution\$name" -Destination $launcher }
foreach ($name in @('LICENSE','ASSETS.md')) { Copy-Item -LiteralPath "$root\$name" -Destination $launcher }
Copy-Item -LiteralPath "$root\launcher\gemini\VALIDATION.md" -Destination $launcher
Copy-Item -LiteralPath "$root\VALIDATION.md" -Destination $full
Copy-Item -LiteralPath "$root\distribution\START-HERE.txt" -Destination $full
$standalone = Join-Path $OutputDirectory "Gemini-Launcher-v$Version-win-x64"
Copy-Item -LiteralPath $launcher -Destination $standalone -Recurse
$archives = foreach ($directory in @($full,$standalone)) {
    Set-Content -LiteralPath (Join-Path $directory 'VERSION.txt') -Value $Version -Encoding utf8
    $files = @(Get-ChildItem -LiteralPath $directory -File -Recurse | Sort-Object FullName)
    foreach ($file in $files) {
        if ($file.Extension -in @('.lnk','.pdb','.log') -or $file.Name -in @('targets.xml','installation.json','original.json') -or $file.Name -like '*Tests.exe*') { throw "Private or test artifact in package: $($file.Name)" }
    }
    $lines = foreach ($file in $files) { (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($directory,$file.FullName).Replace('\','/') }
    Set-Content -LiteralPath (Join-Path $directory 'SHA256SUMS.txt') -Value $lines -Encoding utf8
    $archive = $directory + '.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($directory,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
    [pscustomobject]@{Name=[IO.Path]::GetFileName($archive);Path=$archive;SHA256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();Bytes=(Get-Item -LiteralPath $archive).Length;PayloadFiles=$files.Count+1}
}
$archives | ForEach-Object { $_.SHA256 + '  ' + $_.Name } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
$archives | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package-evidence.json') -Encoding utf8
$archives | Format-Table Name,Bytes,PayloadFiles,SHA256
Write-Output "RELEASE_DIRECTORY: $OutputDirectory"
