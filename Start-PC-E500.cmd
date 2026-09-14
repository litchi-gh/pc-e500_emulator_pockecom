@echo off
cd /d "%~dp0"
if not exist "app\PC-E500.exe" (
  echo Please run build.ps1 first.
  pause
  exit /b 1
)
start "" "app\PC-E500.exe" %*
