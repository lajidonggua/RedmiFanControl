param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'dist'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'Restore-Dependencies.ps1') -Offline
$exe=Join-Path $root 'app\bin\RedmiFanControl.exe'
if(!(Test-Path $exe)){throw 'Build first with scripts/Test.ps1'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
$version=($version -split '\.')[0..2] -join '.'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip=Join-Path $OutputDirectory ('RedmiFanControl-'+$version+'-win-x64.zip')
$standalone=Join-Path $OutputDirectory ('RedmiFanControl-'+$version+'-win-x64.exe')
if((Test-Path $zip) -or (Test-Path $standalone) -or (Test-Path (Join-Path $OutputDirectory 'SHA256SUMS.txt'))){throw 'Release output already exists. Choose a new OutputDirectory; existing releases are not overwritten.'}
$stage=Join-Path $OutputDirectory ('stage-'+[Guid]::NewGuid().ToString('N'))
$runtime=Join-Path $stage 'RedmiFanControl'
New-Item -ItemType Directory -Path (Join-Path $runtime 'config'),(Join-Path $runtime 'docs') -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $runtime 'RedmiFanControl.exe')
Copy-Item -LiteralPath $exe -Destination $standalone
foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','dependencies.lock.json','CONTRIBUTING.md','SECURITY.md')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $runtime}
Copy-Item -LiteralPath (Join-Path $root 'config\fan-control.example.json') -Destination (Join-Path $runtime 'config')
foreach($file in @('SAFETY.md','ARCHITECTURE.md','PUBLISHING.md','CONTROL_LOGIC_REVIEW.md','REFACTOR-0.9.md')){Copy-Item -LiteralPath (Join-Path $root ('docs\'+$file)) -Destination (Join-Path $runtime 'docs')}
Copy-Item -LiteralPath (Join-Path $root 'docs\images') -Destination (Join-Path $runtime 'docs\images') -Recurse
$lock=Get-Content (Join-Path $root 'dependencies.lock.json') -Raw | ConvertFrom-Json
$modules='licenses\pawnio-modules-'+$lock.version
New-Item -ItemType Directory -Path (Join-Path $runtime $modules) -Force | Out-Null
foreach($file in @('COPYING','source.zip')){Copy-Item -LiteralPath (Join-Path $root ('deps\pawnio-modules-'+$lock.version+'\'+$file)) -Destination (Join-Path $runtime $modules)}
Compress-Archive -Path $runtime -DestinationPath $zip -CompressionLevel Optimal
$checksums=@($zip,$standalone) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),($checksums -join [Environment]::NewLine)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Output $zip
Write-Output $standalone
