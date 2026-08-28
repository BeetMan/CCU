@echo off
setlocal

:: ============================================================
::  CCU Alternative — 卸载脚本 (需管理员权限)
:: ============================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 请以管理员身份运行此脚本！
    pause
    exit /b 1
)

echo 正在卸载 CCU Alternative...

:: 1. 停止并删除服务
echo [1/4] 停止服务...
sc.exe stop  CCUService >nul 2>&1
sc.exe delete CCUService >nul 2>&1

:: 2. 删除快捷方式
echo [2/4] 删除快捷方式...
del /f "%ProgramData%\Microsoft\Windows\Start Menu\Programs\CCU Alternative.lnk" >nul 2>&1
del /f "%UserProfile%\Desktop\CCU Alternative.lnk" >nul 2>&1

:: 3. 从 PATH 中移除
echo [3/4] 清理 PATH...
powershell -NoProfile -Command ^
    "$p=[Environment]::GetEnvironmentVariable('Path','Machine');" ^
    "$p=$p.Replace(';C:\Program Files\CCU Alternative\CCU.Cli','');" ^
    "[Environment]::SetEnvironmentVariable('Path',$p,'Machine')"

:: 4. 删除安装目录
echo [4/4] 删除文件...
rmdir /S /Q "C:\Program Files\CCU Alternative" 2>nul

echo.
echo CCU Alternative 已卸载。
pause
