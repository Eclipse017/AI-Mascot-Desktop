#requires -Version 7.0
param([Parameter(Mandatory)][string]$PackagePath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/validation/console-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$manager=Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'AIMascot.Console.exe'
$roleIds=@('dragon','gemini','grok','claude','deepseek')
$script:sequence=0
$script:claudeSamples=[Collections.Generic.List[object]]::new()
$checks=[ordered]@{}
function Check([bool]$Value,[string]$Name){$checks[$Name]=$Value;if(!$Value){throw "FAIL: $Name"}}
function Run-Manager([string[]]$Arguments,[int]$Timeout=45000) {
 $p=Start-Process -FilePath $manager -ArgumentList $Arguments -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit($Timeout) -or $p.ExitCode -ne 0){throw ('Manager failed: '+($Arguments -join ' '))}
}
function Read-State {
 $script:sequence++;$report=Join-Path $OutputDirectory ('status-'+$script:sequence+'.json')
 Run-Manager @('--status',('"'+$report+'"'))
 $script:claudeSamples.Add(@{Utc=[DateTime]::UtcNow.ToString('o');ClientPids=@(Get-Process -Name Claude -ErrorAction SilentlyContinue | ForEach-Object Id)})
 @(Get-Content -LiteralPath $report -Raw|ConvertFrom-Json)
}
function Send-Command([string]$Role,[string]$Command) {
 $script:sequence++;$report=Join-Path $OutputDirectory ('command-'+$script:sequence+'-'+$Role+'-'+$Command+'.json')
 Run-Manager @('--command',$Role,$Command,('"'+$report+'"'))
 $r=@(Get-Content -LiteralPath $report -Raw|ConvertFrom-Json)
 if(@($r|Where-Object {!$_.Success}).Count -or $r.Count -ne $(if($Role -eq 'all'){5}else{1})){throw "Unconfirmed command: $Role $Command"}
 Start-Sleep -Milliseconds 300
}
function Role-State([string]$Role){Read-State | Where-Object Id -eq $Role}
function Same-Options($a,$b){($a|ConvertTo-Json -Compress) -eq ($b|ConvertTo-Json -Compress)}
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class MascotConsoleWindowTest { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l); }'
$initial=Read-State
if(@($initial|Where-Object Running).Count){throw "Exit the public package mascots before this verification; no running hosts were changed."}
Send-Command all start
$before=Read-State
# The process and IPC events exist before WPF finishes loading its first images.
$readyDeadline=[DateTime]::UtcNow.AddSeconds(30)
while(@($before|Where-Object {!$_.Options -or !$_.Running}).Count -and [DateTime]::UtcNow -lt $readyDeadline){
 Start-Sleep -Milliseconds 500
 $before=Read-State
}
Check ((($before.Id|Sort-Object) -join ',') -eq (($roleIds|Sort-Object) -join ',')) 'Exactly five portable role records'
if(@($before|Where-Object {!$_.Installed -or !$_.Running -or !$_.Options -or $_.OtherCopy}).Count){Send-Command all quit; throw 'Portable hosts did not become ready within 30 seconds'}
$before|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'before.json')
foreach($role in $roleIds){Copy-Item -LiteralPath (Join-Path $env:LOCALAPPDATA "AI-Mascot-Desktop/$role/preferences.json") -Destination (Join-Path $OutputDirectory ($role+'-preferences-before.json'))}
$failure=$null
try {
 foreach($id in $roleIds){
  $peers=Read-State | Where-Object Id -ne $id
  $initial=Role-State $id
  Send-Command $id demo; Send-Command $id restore; Send-Command $id hide
  $current=Role-State $id
  Check ($current.Options.Mode -eq 1 -and $current.Options.UserHidden -and !$current.Visible) ($id+' individual mode and hide')
  Send-Command $id start
  $current=Role-State $id
  Check ($current.Options.UserHidden -and ($current.ProcessIds -join ',') -eq ($initial.ProcessIds -join ',')) ($id+' duplicate start preserves hide and PID')
  Send-Command $id quit
  Check (!(Role-State $id).Running) ($id+' exits independently')
  $now=Read-State
  foreach($peer in $peers){
   $current=$now|Where-Object Id -eq $peer.Id
   Check (($current.ProcessIds -join ',') -eq ($peer.ProcessIds -join ',') -and (Same-Options $current.Options $peer.Options)) ($id+' leaves '+$peer.Id+' PID and options intact')
  }
  Send-Command $id start
  Check ((Role-State $id).Options.UserHidden) ($id+' cold restart preserves hide')
  $paused=(Role-State $id).Options.Paused
  Send-Command $id pause; Check ((Role-State $id).Options.Paused -ne $paused) ($id+' pause acknowledged'); Send-Command $id pause
  Send-Command $id pet; Send-Command $id greet
 }
 Send-Command all hide; Send-Command all auto
 foreach($current in Read-State){Check ($current.Options.Mode -eq 0 -and $current.Options.UserHidden -and !$current.Visible) ($current.Id+' batch follow keeps manual hide')}
 Send-Command all demo; Send-Command all restore
 Start-Sleep -Milliseconds 1200
 foreach($current in Read-State){Check ($current.Options.Mode -eq 1 -and !$current.Options.UserHidden) ($current.Id+' batch demo and restore acknowledged')}
 Run-Manager @('--verify',('"'+(Join-Path $OutputDirectory 'layout')+'"'))
 Send-Command all hide; Send-Command all quit
 foreach($current in Read-State){Check (!$current.Running) ($current.Id+' batch quit stops host')}
 Send-Command all start
 foreach($current in Read-State){Check ($current.Running -and $current.Options.UserHidden) ($current.Id+' batch cold start preserves hide')}
 $hosts=Read-State
 $panel=Start-Process -FilePath $manager -WindowStyle Hidden -PassThru
 Start-Sleep -Milliseconds 2000
 $panel.Refresh()
 Check (!$panel.HasExited -and $panel.MainWindowHandle -ne 0) 'Installed console opens a real window'
 $duplicate=Start-Process -FilePath $manager -WindowStyle Hidden -PassThru
 Check ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) 'Repeated console open activates existing instance'
 $instances=@(Get-Process -Name AIMascot.Console -ErrorAction SilentlyContinue | Where-Object Path -eq $manager)
 Check ($instances.Count -eq 1 -and $instances[0].Id -eq $panel.Id) 'Exactly one installed console instance'
 Check ([MascotConsoleWindowTest]::PostMessage($panel.MainWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) -and $panel.WaitForExit(10000)) 'Native WM_CLOSE closes the console panel'
 $now=Read-State
 foreach($hostState in $hosts){$current=$now|Where-Object Id -eq $hostState.Id; Check (($current.ProcessIds -join ',') -eq ($hostState.ProcessIds -join ',')) ($hostState.Id+' survives console close with same PID')}
} catch {$failure=$_}
finally {
 foreach($original in $before){
  try {
   Send-Command $original.Id start
   Send-Command $original.Id $(if($original.Options.Mode -eq 1){'demo'}else{'auto'})
   Send-Command $original.Id $(if($original.Options.UserHidden){'hide'}else{'restore'})
   if((Role-State $original.Id).Options.Paused -ne $original.Options.Paused){Send-Command $original.Id pause}
  } catch {if(!$failure){$failure=$_}}
 }
 $after=Read-State
 foreach($original in $before){
  $current=$after|Where-Object Id -eq $original.Id
  $checks[$original.Id+' original display options restored']=Same-Options $current.Options $original.Options
  $checks[$original.Id+' original startup preserved']=$current.StartupEnabled -eq $original.StartupEnabled
 }
 $checks['Claude client absent at every sample']=@($script:claudeSamples|Where-Object {$_.ClientPids.Count -gt 0}).Count -eq 0
 $passed=!$failure -and @($checks.Values|Where-Object {$_ -ne $true}).Count -eq 0
 @{Passed=$passed;Checks=$checks;Before=$before;After=$after;ClaudeProcessSamples=$script:claudeSamples.ToArray();Failure=if($failure){$failure.ToString()}else{$null};Scope='Five portable local mascot hosts; real IPC and console lifecycle; no AI client launched or stopped; display options restored via IPC; latest positions retained; no startup writes.'}|ConvertTo-Json -Depth 9|Set-Content (Join-Path $OutputDirectory 'integration.json')
}
Send-Command all quit
if(!$passed){throw "Integration failed: $failure"}
"PASS: $($checks.Count) portable integration assertions"
