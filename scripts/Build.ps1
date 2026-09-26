#requires -Version 7.0
param([switch]$Publish, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:AI_MASCOT_DOTNET) { $env:AI_MASCOT_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$projects = [ordered]@{
 Dragon = 'src/AIMascot.Windows/AIMascot.Windows.csproj'
 Gemini = 'roles/gemini/src/GeminiMascot.Windows/GeminiMascot.Windows.csproj'
 Grok = 'roles/grok/src/GrokMascot.Windows/GrokMascot.Windows.csproj'
 Claude = 'roles/claude/src/ClaudeMascot.Windows/ClaudeMascot.Windows.csproj'
 DeepSeek = 'roles/deepseek/src/DeepSeekMascot.Windows/DeepSeekMascot.Windows.csproj'
 Console = 'console/src/MascotConsole/MascotConsole.csproj'
}
$tests = @('tests/AIMascot.Core.Tests', 'tests/AIMascot.Windows.Tests',
 'roles/gemini/tests/GeminiMascot.Windows.Tests', 'roles/grok/tests/GrokMascot.Windows.Tests',
 'roles/claude/tests/ClaudeMascot.Windows.Tests', 'roles/deepseek/tests/DeepSeekMascot.Windows.Tests',
 'console/tests/MascotConsole.Tests', 'tests/TargetPaths.Tests')
Push-Location $root
try {
 foreach ($test in $tests) {
  & $dotnet run --project $test -c Release
  if ($LASTEXITCODE) { throw "Failed: $test" }
 }
 if ($Publish) {
  if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/releases/AI-Mascot-Desktop-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
  $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
  if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing packages are preserved.' }
  New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
  $manifest = @{}
  $staging = Join-Path $root ('artifacts/publish/' + [guid]::NewGuid().ToString('N'))
  foreach ($role in $projects.Keys) {
   $part = Join-Path $staging $role
   & $dotnet publish $projects[$role] -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $part
   if ($LASTEXITCODE) { throw "Publish failed: $role" }
   foreach ($file in Get-ChildItem -LiteralPath $part -File -Recurse) {
    if ($file.Extension -eq '.pdb') { continue }
    $relative = [IO.Path]::GetRelativePath($part, $file.FullName)
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
    if ($manifest.ContainsKey($relative)) {
     if ($manifest[$relative] -ne $hash) { throw "Shared runtime file differs: $relative" }
     continue
    }
    $target = Join-Path $OutputDirectory $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $manifest[$relative] = $hash
   }
  }
  foreach ($name in @('README.md','LICENSE','ASSETS.md','THIRD-PARTY-NOTICES.md')) {
   Copy-Item -LiteralPath (Join-Path $root $name) -Destination $OutputDirectory
  }
  foreach ($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {
   $notice = Join-Path (Split-Path $dotnet) $name
   if (!(Test-Path -LiteralPath $notice)) { throw "Missing .NET redistribution notice: $notice" }
   Copy-Item -LiteralPath $notice -Destination $OutputDirectory
  }
  $manifest | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'runtime-sha256.json') -Encoding utf8
  Write-Output "PACKAGE: $OutputDirectory"
 }
} finally { Pop-Location }
