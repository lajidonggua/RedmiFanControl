param([switch]$Offline)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$lock=Get-Content (Join-Path $root 'dependencies.lock.json') -Raw | ConvertFrom-Json
$target=Join-Path $root ('deps\pawnio-modules-'+$lock.version)
$cache=Join-Path $root '.cache'
function Test-Hash([string]$path,[string]$hash){return (Test-Path -LiteralPath $path) -and ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $hash)}
$complete=Test-Hash (Join-Path $target 'COPYING') $lock.licenseSha256
foreach($module in $lock.modules){if(!(Test-Hash (Join-Path $target $module.name) $module.sha256)){$complete=$false}}
if(!(Test-Hash (Join-Path $target 'source.zip') $lock.sourceSha256)){$complete=$false}
if($complete){Write-Output 'Pinned module binaries and corresponding source verified.';return}
if($Offline){throw 'Pinned dependencies missing or invalid. Run scripts/Restore-Dependencies.ps1 with network access.'}
New-Item -ItemType Directory -Path $cache,$target -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$archive=Join-Path $cache ('PawnIO.Modules-'+$lock.version+'.zip')
$source=Join-Path $cache ('PawnIO.Modules-'+$lock.version+'-source.zip')
if(!(Test-Path $archive)){Invoke-WebRequest -Uri $lock.archiveUrl -OutFile $archive -UseBasicParsing}
if(!(Test-Hash $archive $lock.archiveSha256)){throw 'Official module archive SHA-256 mismatch; refusing to use it.'}
if(!(Test-Path $source)){Invoke-WebRequest -Uri $lock.sourceUrl -OutFile $source -UseBasicParsing}
if(!(Test-Hash $source $lock.sourceSha256)){throw 'Corresponding source SHA-256 mismatch; refusing to use it.'}
$unpack=Join-Path $cache ('unpack-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $unpack
foreach($module in $lock.modules){
  $candidates=@(Get-ChildItem -LiteralPath $unpack -Recurse -File | Where-Object Name -eq $module.name)
  if($candidates.Count -ne 1 -or !(Test-Hash $candidates[0].FullName $module.sha256)){throw ('Pinned module missing or invalid: '+$module.name)}
  Copy-Item -LiteralPath $candidates[0].FullName -Destination (Join-Path $target $module.name)
}
$license=@(Get-ChildItem -LiteralPath $unpack -Recurse -File | Where-Object Name -eq 'COPYING')
if($license.Count -ne 1 -or !(Test-Hash $license[0].FullName $lock.licenseSha256)){throw 'Upstream COPYING missing, ambiguous or hash mismatch.'}
Copy-Item -LiteralPath $license[0].FullName -Destination (Join-Path $target 'COPYING')
Copy-Item -LiteralPath $source -Destination (Join-Path $target 'source.zip')
Write-Output 'Verified official modules, license and exact corresponding source restored. No driver installed or hardware accessed.'
