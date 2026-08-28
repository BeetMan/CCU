@echo off
echo ============================================
echo   CCU.Service Windows Service 安装程序
echo   需要管理员权限
echo ============================================
echo.
echo 如果提示 UAC，请点击"是"。
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 正在请求管理员权限...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo 正在安装 CCU.Service...
powershell -ExecutionPolicy Bypass -File "%~dp0install-service.ps1"
echo.
pause
