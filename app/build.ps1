param([string]$OutputName='bin')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'scripts\Restore-Dependencies.ps1') -Offline
if($OutputName -notmatch '^bin(?:-[a-zA-Z0-9]+)?$'){throw 'Invalid build output folder'}
$output=Join-Path $PSScriptRoot $OutputName
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@((Join-Path $root 'tools\StrictSuperIoProbe.cs'),(Join-Path $root 'tools\StrictIteFanControl.cs'))+@(Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
if(!(Test-Path (Join-Path $PSScriptRoot 'assets\fan.ico'))){& (Join-Path $PSScriptRoot 'assets\Generate-Icon.ps1')}
$arguments=@('/nologo','/codepage:65001','/optimize+','/target:winexe','/platform:x64',('/out:'+(Join-Path $output 'RedmiFanControl.exe')),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll')+$sources
$arguments+=@(('/win32icon:'+(Join-Path $PSScriptRoot 'assets\fan.ico')),('/resource:'+(Join-Path $PSScriptRoot 'assets\fan.ico')+',FanControl.Icon'),('/resource:'+(Join-Path $PSScriptRoot 'assets\fan.png')+',FanControl.Image'))
$arguments+=('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest'))
foreach($file in @('LpcIO.bin','AMDFamily17.bin','COPYING','source.zip')){
  $arguments+=('/resource:'+(Join-Path $root ('deps\pawnio-modules-0.2.11\'+$file))+',FanControl.Runtime.'+$file)
}
foreach($file in @('LICENSE','THIRD-PARTY-NOTICES.md')){$arguments+=('/resource:'+(Join-Path $root $file)+',FanControl.Notice.'+$file)}
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
Write-Output (Join-Path $output 'RedmiFanControl.exe')
