@echo off
if not exist "%~dp0app\bin\RedmiFanControl.exe" (
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\build.ps1"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0app\bin\RedmiFanControl.exe"
