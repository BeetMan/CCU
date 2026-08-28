@echo off
echo === CCUService v14 ===
net stop CCUService
timeout /t 2 /nobreak >nul
sc.exe config CCUService binPath= "D:\ID6 Control Center\CCU.Alternative\publish\CCU.Service.v14\CCU.Service.exe" obj= LocalSystem
net start CCUService
sc.exe query CCUService
echo Done.
pause
