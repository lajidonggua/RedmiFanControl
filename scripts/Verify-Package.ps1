param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$Archive=(Resolve-Path -LiteralPath $Archive).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($Archive)
try {
  $names=@($zip.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
  foreach($name in $names){
    if(!$name.StartsWith('RedmiFanControl/',[StringComparison]::Ordinal) -or $name -match '(^|/)\.\.(/|$)|(^|/)(runs|\.git|\.cache|sources)/|config/fan-control\.json$|\.bak$|ec-region|DSDT|PawnIOLib\.dll|PawnIOSetup'){throw ('Unexpected private/unsafe payload: '+$name)}
  }
  foreach($name in @('RedmiFanControl/app/bin/RedmiFanControl.exe','RedmiFanControl/deps/pawnio-modules-0.2.11/source.zip','RedmiFanControl/deps/pawnio-modules-0.2.11/COPYING','RedmiFanControl/LICENSE','RedmiFanControl/docs/ARCHITECTURE.md')){
    if($names -notcontains $name){throw ('Required release file missing: '+$name)}
  }
} finally {$zip.Dispose()}
Write-Output ('Package inventory PASS ('+$names.Count+' entries).')
$temp=Join-Path $root ('.cache\p-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
Expand-Archive -LiteralPath $Archive -DestinationPath $temp
$exe=Join-Path $temp 'RedmiFanControl\app\bin\RedmiFanControl.exe'
$process=Start-Process -FilePath $exe -ArgumentList '--selftest' -WindowStyle Hidden -PassThru -Wait
$report=Get-Content (Join-Path $temp 'RedmiFanControl\app\selftest.txt') -Raw
if($process.ExitCode -ne 0 -or $report -notmatch '^PASS:'){throw ('Extracted selftest failed: '+$report)}
Write-Output $report
Write-Output 'Independent extracted package PASS. No hardware accessed; no startup registry changes.'
