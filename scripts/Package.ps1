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
if(Test-Path $zip){throw 'Output archive already exists. Choose a new OutputDirectory; existing releases are not overwritten.'}
$stage=Join-Path $OutputDirectory ('stage-'+[Guid]::NewGuid().ToString('N'))
$runtime=Join-Path $stage 'RedmiFanControl'
New-Item -ItemType Directory -Path (Join-Path $runtime 'app\bin'),(Join-Path $runtime 'config'),(Join-Path $runtime 'docs') -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $runtime 'app\bin\RedmiFanControl.exe')
foreach($file in @('Start-FanControl.cmd','README.md','LICENSE','THIRD-PARTY-NOTICES.md','dependencies.lock.json','CONTRIBUTING.md','SECURITY.md')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $runtime}
Copy-Item -LiteralPath (Join-Path $root 'config\fan-control.example.json') -Destination (Join-Path $runtime 'config')
foreach($file in @('SAFETY.md','ARCHITECTURE.md','PUBLISHING.md','CONTROL_LOGIC_REVIEW.md')){Copy-Item -LiteralPath (Join-Path $root ('docs\'+$file)) -Destination (Join-Path $runtime 'docs')}
$lock=Get-Content (Join-Path $root 'dependencies.lock.json') -Raw | ConvertFrom-Json
$modules='deps\pawnio-modules-'+$lock.version
New-Item -ItemType Directory -Path (Join-Path $runtime $modules) -Force | Out-Null
foreach($file in @('LpcIO.bin','AMDFamily17.bin','COPYING','source.zip')){Copy-Item -LiteralPath (Join-Path $root ($modules+'\'+$file)) -Destination (Join-Path $runtime $modules)}
Compress-Archive -Path $runtime -DestinationPath $zip -CompressionLevel Optimal
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),$hash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Output $zip
