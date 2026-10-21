#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new extraction directory.' }
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
try {
    foreach ($entry in $zip.Entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $OutputDirectory $entry.FullName))
        if (!$target.StartsWith($OutputDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP contains an escaping path.' }
    }
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Archive),$OutputDirectory)
$directories = @(Get-ChildItem -LiteralPath $OutputDirectory -Directory)
if ($directories.Count -ne 1) { throw 'Expected one top-level package directory.' }
$package = $directories[0].FullName
$expected = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'Malformed package manifest.' }
    $relative = $Matches[2]; $hash = $Matches[1]
    $target = [IO.Path]::GetFullPath((Join-Path $package $relative))
    if (!$target.StartsWith($package + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $expected.ContainsKey($relative)) { throw 'Invalid or duplicate manifest path.' }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $hash) { throw "Manifest mismatch: $relative" }
    $expected[$relative] = $hash
}
$actual = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/') } | Where-Object { $_ -ne 'SHA256SUMS.txt' })
if (@(Compare-Object @($expected.Keys | Sort-Object) @($actual | Sort-Object)).Count) { throw 'Manifest inventory mismatch.' }
$launcher = if (Test-Path -LiteralPath (Join-Path $package 'Gemini-Launcher')) { Join-Path $package 'Gemini-Launcher' } else { $package }
& "$root\launcher\gemini\scripts\Test-Installer.ps1" -InstallerScript "$launcher\scripts\Install.ps1" -PayloadDirectory "$launcher\app"
$result = [pscustomobject]@{Status='PASS';Archive=[IO.Path]::GetFileName($Archive);SHA256=(Get-FileHash -LiteralPath $Archive).Hash.ToLowerInvariant();ManifestFiles=$expected.Count;TotalFiles=$actual.Count+1;InstallerChecks=10;AIClientsStarted=$false;ExtractedPackage=$package}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Encoding utf8
$result | ConvertTo-Json
