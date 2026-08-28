# ============================================================
# CCU Alternative — Windows Service 卸载脚本
# 用法: 以管理员身份运行
#   powershell -NoProfile -ExecutionPolicy Bypass -File uninstall-service.ps1
# ============================================================

param(
    [string]$ServiceName = "CCUService",
    [string]$InstallDir = "C:\Program Files\CCU Alternative",
    [switch]$KeepFiles
)

$ErrorActionPreference = "Continue"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[FAIL] Please run as Administrator!" -ForegroundColor Red
    exit 1
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CCU Alternative — Uninstaller" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# 1. Stop & delete service
Write-Host "  [1/4] Stopping service..." -ForegroundColor Cyan
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }
    sc.exe delete $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "    [OK] $ServiceName removed" -ForegroundColor Green
} else {
    Write-Host "    [OK] $ServiceName not found" -ForegroundColor Green
}

# 2. Delete shortcuts
Write-Host "  [2/4] Removing shortcuts..." -ForegroundColor Cyan
$shortcuts = @(
    "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\CCU Alternative.lnk"
    "$env:AppData\Microsoft\Windows\Start Menu\Programs\CCU Alternative.lnk"
    "$env:UserProfile\Desktop\CCU Alternative.lnk"
    "$env:Public\Desktop\CCU Alternative.lnk"
)
foreach ($sc in $shortcuts) {
    Remove-Item $sc -Force -ErrorAction SilentlyContinue
}
Write-Host "    [OK] Shortcuts removed" -ForegroundColor Green

# 3. Clean PATH
Write-Host "  [3/4] Cleaning PATH..." -ForegroundColor Cyan
try {
    $path = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $cliPath = "$InstallDir\CCU.Cli"
    if ($path -like "*$cliPath*") {
        $newPath = ($path -split ";" | Where-Object { $_ -ne $cliPath -and $_ -ne "$cliPath\" }) -join ";"
        [Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
        Write-Host "    [OK] CLI path removed from system PATH" -ForegroundColor Green
    }
} catch {
    Write-Host "    [WARN] PATH cleanup failed: $_" -ForegroundColor Yellow
}

# 4. Delete files
if (-not $KeepFiles) {
    Write-Host "  [4/4] Removing files..." -ForegroundColor Cyan
    if (Test-Path $InstallDir) {
        Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
        Write-Host "    [OK] $InstallDir removed" -ForegroundColor Green
    }
} else {
    Write-Host "  [4/4] Keeping files at $InstallDir" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CCU Alternative uninstalled" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
