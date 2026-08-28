@echo off
echo ========================================
echo  CCUService v11 更新安装
echo ========================================
echo.
echo 停止服务...
net stop CCUService
echo.
echo 更新二进制路径...
sc config CCUService binPath= "D:\ID6 Control Center\CCU.Alternative\publish\CCU.Service.v11\CCU.Service.exe" obj= LocalSystem
echo.
echo 启动服务...
net start CCUService
echo.
echo 状态:
sc query CCUService
echo.
echo 完成！
pause
