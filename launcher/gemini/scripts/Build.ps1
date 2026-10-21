#requires -Version 7.0
[CmdletBinding()]
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repository = Split-Path (Split-Path $root -Parent) -Parent
$icon = Join-Path $repository 'assets\pets\gemini\gemini.ico'
$portrait = Join-Path $repository 'assets\console\gemini.png'
$out = Join-Path $root 'artifacts\build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$arguments = @('/nologo','/optimize+','/checked+','/langversion:5','/platform:anycpu','/target:winexe',"/out:$out\GeminiLauncher.exe", "/win32manifest:$root\src\app.manifest", "/win32icon:$icon")
foreach ($reference in @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $reference)
}
$arguments += @("/resource:$icon,Assets.Gemini.ico", "/resource:$portrait,Assets.Gemini.png")
$sources = @(Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs' -File | ForEach-Object FullName)
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath "$root\src\App.config" -Destination "$out\GeminiLauncher.exe.config" -Force
Copy-Item -LiteralPath $icon -Destination (Join-Path $out 'Gemini.ico') -Force
if ($Test) {
    $testArguments = @($arguments | Where-Object { $_ -notlike '/out:*' -and $_ -notlike '/target:*' -and $_ -notlike '/win32icon:*' })
    $testArguments += @('/target:exe','/main:GeminiLauncher.Tests',"/out:$out\GeminiLauncher.Tests.exe")
    & $compiler @testArguments @sources "$root\tests\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item -LiteralPath "$root\src\App.config" -Destination "$out\GeminiLauncher.Tests.exe.config" -Force
    & "$out\GeminiLauncher.Tests.exe" "$root\artifacts\tests"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Get-Item -LiteralPath "$out\GeminiLauncher.exe" | Select-Object FullName,Length
