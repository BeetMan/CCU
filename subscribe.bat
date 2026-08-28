@echo off
cd /d "D:\ID6 Control Center\CCU.Alternative"
echo ============================================
echo   CCU MQTT Subscribe — 监听原厂智控中心
echo ============================================
echo.
echo 请确保原厂智控中心正在运行
echo 然后在此窗口中观察 MQTT 消息
echo 按 Ctrl+C 停止
echo.
dotnet run --project src/CCU.Cli -- subscribe %*
pause
