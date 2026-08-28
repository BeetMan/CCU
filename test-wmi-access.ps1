# ============================================================
# CCU Alternative — WMI ACPI Access Verification
# 用法: 以管理员身份运行以测试服务/计划任务
#   powershell -NoProfile -ExecutionPolicy Bypass -File test-wmi-access.ps1
# ============================================================

param(
    [switch]$AsScheduledTask,
    [switch]$ViaService,
    [string]$ServiceName = "CCUService"
)

$ErrorActionPreference = "Continue"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CCU Alternative — WMI ACPI Access Test" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# ==== Test 1: Current context ====
Write-Host "--- Test 1: Current context ---" -ForegroundColor Yellow
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
Write-Host "  User: $($identity.Name)"
Write-Host "  IsSystem: $($identity.IsSystem)"
Write-Host "  IsAdmin: $isAdmin"

# ==== Test 2: Direct WMI access ====
Write-Host ""
Write-Host "--- Test 2: Direct WMI AcpiTest_MULong ---" -ForegroundColor Yellow
try {
    $scope = New-Object System.Management.ManagementScope("\\.\root\wmi")
    $query = New-Object System.Management.ObjectQuery("SELECT * FROM AcpiTest_MULong")
    $searcher = New-Object System.Management.ManagementObjectSearcher($scope, $query)
    $results = @($searcher.Get())

    if ($results.Count -gt 0) {
        Write-Host "  [OK] Found $($results.Count) AcpiTest_MULong instance(s)" -ForegroundColor Green
        $acpi = $results[0]

        # Try reading EC address 0x04CC (performance mode) using GetSetULong
        # SMRW_CMD_READ = 0xBB = 187
        # Format: cmd | (value << 8) | (addr << 16)
        # For read: 0xBB | (0 << 8) | (0x04CC << 16)
        $readData = 187 + (0x04CC -shl 16)  # 0xBB0004CC ... let me compute
        $readData = [uint64]0xBB + ([uint64]0x04CC -shl 16)

        Write-Host "  Attempting GetSetULong(0x$($readData.ToString('X16')))..."

        $inParams = $acpi.GetMethodParameters("GetSetULong")
        $inParams["Data"] = $readData

        $outParams = $acpi.InvokeMethod("GetSetULong", $inParams, $null)
        if ($outParams) {
            $result = $outParams["Return"]
            $ecValue = ($result -shr 8) -band 0xFF
            Write-Host "  [OK] EC Read Success!" -ForegroundColor Green
            Write-Host "  Return: 0x$($result.ToString('X16'))"
            Write-Host "  EC Value (byte): 0x$($ecValue.ToString('X2')) ($ecValue)"
            Write-Host "  Performance Mode: $ecValue"
        } else {
            Write-Host "  [FAIL] GetSetULong returned null" -ForegroundColor Red
        }
    } else {
        Write-Host "  [FAIL] No AcpiTest_MULong instances found" -ForegroundColor Red
        Write-Host "  Check: sc.exe query UWACPIDriver"
    }
} catch {
    Write-Host "  [FAIL] WMI Error: $_" -ForegroundColor Red

    if ($_.Exception.Message -match "拒绝访问|Access.*denied|0x80041003") {
        Write-Host ""
        Write-Host "  === DIAGNOSIS: Access Denied ===" -ForegroundColor Red
        Write-Host "  The WMI AcpiTest_MULong class requires SYSTEM privileges."
        Write-Host "  Current user ($($identity.Name)) does not have SYSTEM access."
        Write-Host ""
        Write-Host "  SOLUTION: Run as Windows Service (LocalSystem):" -ForegroundColor Yellow
        Write-Host "    powershell -NoProfile -ExecutionPolicy Bypass -File install-service.ps1"
    }
}

# ==== Test 3: Via Scheduled Task (SYSTEM) ====
if ($AsScheduledTask -and $isAdmin) {
    Write-Host ""
    Write-Host "--- Test 3: Scheduled Task as SYSTEM ---" -ForegroundColor Yellow

    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>CCU WMI AcpiTest_MULong Access Test</Description>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
  </Settings>
  <Actions>
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -Command "`$s=New-Object System.Management.ManagementScope('\\.\root\wmi');`$q=New-Object System.Management.ObjectQuery('SELECT * FROM AcpiTest_MULong');`$r=(New-Object System.Management.ManagementObjectSearcher(`$s,`$q)).Get();foreach(`$o in `$r){`$p=`$o.GetMethodParameters('GetSetULong');`$p['Data']=0xBB+([uint64]0x04CC -shl 16);`$out=`$o.InvokeMethod('GetSetULong',`$p,`$null);Write-Host 'EC Return:' `$out['Return'];Write-Host 'Value:' (([byte])(`$out['Return'] -shr 8) -band 0xFF)}"</Arguments>
    </Exec>
  </Actions>
</Task>
"@
    $taskPath = "$env:TEMP\ccu-wmi-test.xml"
    Set-Content -Path $taskPath -Value $taskXml

    Write-Host "  Creating scheduled task..."
    schtasks.exe /Create /TN "CCU_WMI_Test" /XML "$taskPath" /F 2>&1
    Write-Host "  Running task..."
    schtasks.exe /Run /TN "CCU_WMI_Test" 2>&1
    Start-Sleep -Seconds 3
    Write-Host "  Checking result..."
    # The task output goes to the task scheduler log
    Write-Host "  (Check Task Scheduler UI for output under CCU_WMI_Test)"
    Write-Host "  Cleanup: schtasks.exe /Delete /TN CCU_WMI_Test /F"
}

# ==== Test 4: Service status ====
Write-Host ""
Write-Host "--- Test 4: CCUService Status ---" -ForegroundColor Yellow
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "  Name: $($svc.Name)"
    Write-Host "  Status: $($svc.Status)"
    Write-Host "  StartType: $($svc.StartType)"

    $config = sc.exe qc $ServiceName 2>&1
    Write-Host "  Config:"
    $config | ForEach-Object { Write-Host "    $_" }
} else {
    Write-Host "  Service $ServiceName not installed" -ForegroundColor Yellow
    Write-Host "  Run install-service.ps1 to install"
}

# ==== Test 5: Pipe connectivity ====
Write-Host ""
Write-Host "--- Test 5: Named Pipe Connectivity ---" -ForegroundColor Yellow
$pipeName = "\\.\pipe\CCU.Service.Pipe"
if ([System.IO.Directory]::Exists("\\.\pipe\") -or $true) {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "CCU.Service.Pipe", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(2000)
        if ($pipe.IsConnected) {
            Write-Host "  [OK] Connected to Named Pipe CCU.Service.Pipe" -ForegroundColor Green
            $pipe.Close()
        }
    } catch {
        Write-Host "  [INFO] Pipe not available — service may not be running" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Test Complete" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
