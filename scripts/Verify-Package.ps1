param([string]$Archive,[string]$Executable)
$ErrorActionPreference='Stop'
if((!$Archive -and !$Executable) -or ($Archive -and $Executable)){throw 'Specify either -Archive or -Executable'}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path $root ('.cache\p-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $temp | Out-Null
if($Archive){
$Archive=(Resolve-Path -LiteralPath $Archive).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($Archive)
try {
  $names=@($zip.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
  foreach($name in $names){
    if(!$name.StartsWith('RedmiFanControl/',[StringComparison]::Ordinal) -or $name -match '(^|/)\.\.(/|$)|(^|/)(runs|\.git|\.cache|sources)/|config/fan-control\.json$|\.(bak|cmd|bat)$|ec-region|DSDT|PawnIOLib\.dll|PawnIOSetup'){throw ('Unexpected private/unsafe payload: '+$name)}
  }
  foreach($name in @('RedmiFanControl/RedmiFanControl.exe','RedmiFanControl/licenses/pawnio-modules-0.2.11/source.zip','RedmiFanControl/licenses/pawnio-modules-0.2.11/COPYING','RedmiFanControl/LICENSE','RedmiFanControl/docs/ARCHITECTURE.md')){
    if($names -notcontains $name){throw ('Required release file missing: '+$name)}
  }
} finally {$zip.Dispose()}
Write-Output ('Package inventory PASS ('+$names.Count+' entries, no CMD/BAT).')
Expand-Archive -LiteralPath $Archive -DestinationPath $temp
$exe=Join-Path $temp 'RedmiFanControl\RedmiFanControl.exe'
}else{
  # Test with only the EXE in this directory: no sibling dependency folder.
  $exe=Join-Path $temp 'RedmiFanControl.exe'
  Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Executable).Path -Destination $exe
}
$testOutput=Join-Path $temp 'test-results'
$process=Start-Process -FilePath $exe -ArgumentList @('--selftest',('"'+$testOutput+'"')) -WindowStyle Hidden -PassThru -Wait
$report=Get-Content (Join-Path $testOutput 'selftest.txt') -Raw
if($process.ExitCode -ne 0 -or $report -notmatch '^PASS:'){throw ('Extracted selftest failed: '+$report)}
Write-Output $report
Write-Output 'Independent executable PASS. Embedded modules/source extracted and verified; no hardware or real user settings accessed.'
