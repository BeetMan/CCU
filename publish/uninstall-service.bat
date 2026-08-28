@echo off
echo ============================================
echo   CCU.Service 卸载程序
echo ============================================
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)
powershell -ExecutionPolicy Bypass -File "%~dp0uninstall-service.ps1"
pause
