@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-service-v22.ps1"
set CODE=%ERRORLEVEL%
echo.
if not "%CODE%"=="0" echo Deployment failed or rolled back. Exit code: %CODE%
pause
exit /b %CODE%
