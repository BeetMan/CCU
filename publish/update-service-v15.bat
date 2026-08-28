@echo off
echo === CCUService v15 ===
net stop CCUService
timeout /t 2 /nobreak >nul
sc.exe config CCUService binPath= "D:\ID6 Control Center\CCU.Alternative\publish\CCU.Service.v16\CCU.Service.exe" obj= LocalSystem
net start CCUService
sc.exe query CCUService
echo Done.
pause
