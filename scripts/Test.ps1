param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'app\build.ps1')
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$childShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach($test in @('test_ite_readonly.ps1','test_ite_control.ps1')){
  & $childShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root ('tests\'+$test))
  if($LASTEXITCODE -ne 0){throw ('Protocol test failed: '+$test)}
}
$testOutput=Join-Path $root ('.cache\test-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$testProcess=Start-Process -FilePath (Join-Path $root 'app\bin\RedmiFanControl.exe') -ArgumentList @('--selftest',('"'+$testOutput+'"')) -WindowStyle Hidden -PassThru -Wait
$report=Join-Path $testOutput 'selftest.txt'
Get-Content $report
if($testProcess.ExitCode -ne 0 -or (Get-Content $report -Raw) -notmatch '^PASS:'){throw 'Application selftest failed'}
Write-Output 'All offline tests passed. No driver, UAC, registry startup changes, stress test or EC access occurred.'
