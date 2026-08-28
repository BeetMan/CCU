# ============================================================
# CCU Alternative — Windows Service 安装脚本
# 用法: 以管理员身份运行
#   powershell -NoProfile -ExecutionPolicy Bypass -File install-service.ps1
# ============================================================

param(
    [string]$ServiceName = "CCUService",
    [string]$InstallDir = "C:\Program Files\CCU Alternative",
    [switch]$SkipFileCopy
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# === 检查管理员权限 ===
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[FAIL] Please run as Administrator!" -ForegroundColor Red
    Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File install-service.ps1"
    exit 1
}

function Write-Step { param($Msg) Write-Host "  [$Msg]" -ForegroundColor Cyan }
function Write-OK   { param($Msg) Write-Host "    [OK] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "    [WARN] $Msg" -ForegroundColor Yellow }
function Write-Fail { param($Msg) Write-Host "    [FAIL] $Msg" -ForegroundColor Red }

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CCU Service — Windows Service Installer" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ==== 1. Locate / Copy Files ====
Write-Step "1/5 Locate CCU.Service executable"

# Try several locations
$candidates = @(
    "$scriptDir\CCU.Service\CCU.Service.exe"
    "$scriptDir\..\..\publish\CCU.Service\CCU.Service.exe"
    "$InstallDir\CCU.Service\CCU.Service.exe"
    "$scriptDir\..\src\CCU.Service\bin\Release\net8.0\win-x64\publish\CCU.Service.exe"
    "$scriptDir\..\src\CCU.Service\bin\Release\net8.0\win-x64\CCU.Service.exe"
)

$svcExe = $null
foreach ($c in $candidates) {
    $resolved = (Resolve-Path $c -ErrorAction SilentlyContinue)?.Path
    if ($resolved -and (Test-Path $resolved)) {
        $svcExe = $resolved
        Write-OK "Found: $svcExe"
        break
    }
}

if (-not $svcExe) {
    Write-Fail "Cannot find CCU.Service.exe. Run publish.ps1 first."
    Write-Host "  Searched:"
    $candidates | ForEach-Object { Write-Host "    $_" }
    exit 2
}

# Copy files to install dir if needed
if ($svcExe -notlike "$InstallDir\*" -and -not $SkipFileCopy) {
    Write-Step "2/5 Copy files to $InstallDir"
    $srcDir = Split-Path -Parent $svcExe
    if (Test-Path $InstallDir) {
        Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Recurse -Force "$srcDir\*" "$InstallDir\"
    Write-OK "Copied to $InstallDir"
} else {
    Write-Step "2/5 Skip file copy (already in place or --SkipFileCopy)"
}

# ==== 3. Stop and delete existing service ====
Write-Step "3/5 Configure Windows Service"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
        Write-OK "Stopped existing $ServiceName"
    }
    sc.exe delete $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    Write-OK "Deleted existing $ServiceName"
}

# ==== 4. Create service ====
$binPath = "$InstallDir\CCU.Service.exe"
if (-not (Test-Path $binPath)) {
    Write-Warn "Expected $binPath not found, listing directory:"
    Get-ChildItem $InstallDir | Select-Object Name | ForEach-Object { Write-Host "    $($_.Name)" }
    # Try to find it
    $altExe = Get-ChildItem -Path $InstallDir -Filter "CCU.Service.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($altExe) { $binPath = $altExe.FullName }
    else { Write-Fail "Cannot find CCU.Service.exe in $InstallDir"; exit 3 }
}

Write-OK "Service binary: $binPath"

# CRITICAL: sc.exe create quoting
#   binPath= MUST have a space after the equals sign
#   Path with spaces MUST be double-quoted
#   The binPath value seen by SCM is: "C:\Program Files\CCU Alternative\CCU.Service.exe"
$createResult = sc.exe create $ServiceName `
    binPath= "`"$binPath`"" `
    displayName= "CCU Alternative Service" `
    start= auto `
    obj= "LocalSystem" `
    2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Warn "sc.exe create exit code $LASTEXITCODE"
    Write-Host "    Output: $createResult"
}

sc.exe description $ServiceName "MACHENIKE/Mechrevo notebook hardware control service (CCU Alternative)" 2>&1 | Out-Null

# Failure recovery: restart after 5 seconds, 3 times, reset after 24 hours
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 2>&1 | Out-Null

Write-OK "Service $ServiceName registered (LocalSystem, auto-start)"

# ==== 5. Start service ====
Write-Step "5/5 Start service"

try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Start-Sleep -Seconds 3
    $svc = Get-Service -Name $ServiceName
    Write-OK "Service status: $($svc.Status)"
} catch {
    Write-Warn "Start failed: $_"
    Write-Host ""
    Write-Host "  Troubleshooting:" -ForegroundColor Yellow
    Write-Host "    1. Check Application Event Log for .NET errors"
    Write-Host "    2. Verify NLog.config output path is writable"
    Write-Host "    3. Run manually to test:"
    Write-Host "       & '$binPath'"
    Write-Host "    4. Check service details:"
    Write-Host "       sc.exe qc $ServiceName"
}

# ==== Verification ====
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Installation Complete" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "  Service: $($svc.Name) - $($svc.DisplayName)"
    Write-Host "  Status:  $($svc.Status)"
    Write-Host "  Account: LocalSystem"
}

Write-Host ""
Write-Host "  Verify WMI access:" -ForegroundColor Yellow
Write-Host "    sc.exe qc $ServiceName"
Write-Host "    Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Host ""
Write-Host "  Useful commands:" -ForegroundColor Yellow
Write-Host "    sc.exe start $ServiceName"
Write-Host "    sc.exe stop  $ServiceName"
Write-Host "    sc.exe qc    $ServiceName"
