@echo off
setlocal enabledelayedexpansion

:: ============================================================
::  CCU Alternative — 安装脚本
::  以管理员权限运行: 右键 → 以管理员身份运行
:: ============================================================

echo ============================================
echo   CCU Alternative — 安装程序
echo   机械师/机械革命 智控中心替代方案
echo ============================================
echo.

:: ── 检查管理员权限 ──
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 请以管理员身份运行此脚本！
    echo   右键 install.bat → "以管理员身份运行"
    pause
    exit /b 1
)

set "INSTALL_DIR=C:\Program Files\CCU Alternative"

echo [1/4] 创建安装目录...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo [2/4] 复制文件...
:: 复制到安装目录
if exist "%~dp0CCU.Service\CCU.Service.exe" (
    xcopy /E /Y /Q "%~dp0CCU.Service\*" "%INSTALL_DIR%\CCU.Service\" >nul
) else (
    :: 可能在 publish\CCU.Service 下，整个 publish 目录一起复制
    xcopy /E /Y /Q "%~dp0CCU.Service\*" "%INSTALL_DIR%\CCU.Service\" >nul
)
xcopy /E /Y /Q "%~dp0CCU.Wpf\*"     "%INSTALL_DIR%\CCU.Wpf\"     >nul
xcopy /E /Y /Q "%~dp0CCU.Cli\*"     "%INSTALL_DIR%\CCU.Cli\"     >nul

echo [3/4] 注册 Windows 服务...
sc.exe stop  CCUService >nul 2>&1
sc.exe delete CCUService >nul 2>&1

:: sc.exe create 关键语法:
::   binPath= 后面必须有空格
::   带空格路径必须用引号包裹
::   最终 SCM 存储的路径: "C:\Program Files\CCU Alternative\CCU.Service\CCU.Service.exe"
set "BIN_PATH=%INSTALL_DIR%\CCU.Service\CCU.Service.exe"
sc.exe create CCUService ^
    binPath= "%BIN_PATH%" ^
    displayName= "CCU Alternative Service" ^
    start= auto ^
    obj= "LocalSystem" ^
    type= own

if %errorlevel% neq 0 (
    echo [警告] 服务注册返回错误码 %errorlevel%
    echo   尝试使用 PowerShell 安装:
    echo   powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\install-service.ps1"
)

sc.exe description CCUService "MACHENIKE/Mechrevo notebook hardware control service (CCU Alternative)"
sc.exe failure CCUService reset= 86400 actions= restart/5000/restart/5000/restart/5000

echo [4/4] 启动服务...
sc.exe start CCUService
if %errorlevel% neq 0 (
    echo [警告] 服务启动失败
    echo   请检查:
    echo     1. 事件查看器 → Windows 日志 → 应用程序
    echo     2. 手动运行: "%BIN_PATH%"
)

:: ── 快捷方式 ──
echo 创建快捷方式...
powershell -NoProfile -Command ^
    "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\CCU Alternative.lnk');" ^
    "$s.TargetPath='%INSTALL_DIR%\CCU.Wpf\CCU.Wpf.exe';" ^
    "$s.WorkingDirectory='%INSTALL_DIR%\CCU.Wpf';" ^
    "$s.Save()"

powershell -NoProfile -Command ^
    "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('StartMenu')+'\Programs\CCU Alternative.lnk');" ^
    "$s.TargetPath='%INSTALL_DIR%\CCU.Wpf\CCU.Wpf.exe';" ^
    "$s.WorkingDirectory='%INSTALL_DIR%\CCU.Wpf';" ^
    "$s.Save()"

:: CLI PATH
powershell -NoProfile -Command ^
    "$p=[Environment]::GetEnvironmentVariable('Path','Machine');" ^
    "if ($p -notlike '*%INSTALL_DIR%\CCU.Cli*') {" ^
    "  [Environment]::SetEnvironmentVariable('Path', $p + ';%INSTALL_DIR%\CCU.Cli', 'Machine')" ^
    "}"

echo.
echo ============================================
echo   安装完成！
echo ============================================
echo.
echo   安装路径: %INSTALL_DIR%
echo   服务: CCUService (LocalSystem, Auto)
echo   CLI 命令: ccu status
echo   桌面快捷方式: CCU Alternative
echo.
echo   验证: sc.exe qc CCUService
echo   测试: powershell -NoProfile -File "%INSTALL_DIR%\..\..\test-wmi-access.ps1"
echo.
pause
