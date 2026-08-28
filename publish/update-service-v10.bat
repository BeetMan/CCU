@echo off
echo Stopping services...
net stop CCUService 2>&1
net stop GCUBridge 2>&1
echo.
echo Updating CCUService binary path to v10...
sc config CCUService binPath= "D:\ID6 Control Center\CCU.Alternative\publish\CCU.Service.v10\CCU.Service.exe" obj= LocalSystem
echo.
echo Starting CCUService...
net start CCUService
echo.
echo Done.
pause
