# CCU.Service Windows Service 安装脚本 — 需要管理员权限运行
$ErrorActionPreference = "Stop"

$serviceName = "CCUService"
$displayName = "CCU Service — 智控中心硬件服务"
$publishDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $publishDir "CCU.Service.exe"

Write-Host "=== CCU.Service Windows Service 安装 ===" -ForegroundColor Cyan
Write-Host "服务名: $serviceName"
Write-Host "可执行文件: $exePath"
Write-Host ""

# 检查管理员权限
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "错误: 需要管理员权限！请以管理员身份运行 PowerShell 然后重新执行此脚本。" -ForegroundColor Red
    exit 1
}

# 检查文件存在
if (-NOT (Test-Path $exePath)) {
    Write-Host "错误: 找不到 $exePath — 请先执行 dotnet publish" -ForegroundColor Red
    exit 1
}

# 如果服务已存在，先停止并删除
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "已存在旧服务，正在停止并删除..." -ForegroundColor Yellow
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# 创建服务 (LocalSystem = SYSTEM 权限)
Write-Host "正在创建 Windows Service (LocalSystem)..." -ForegroundColor Green
$binPath = "`"$exePath`""
sc.exe create $serviceName binPath= $binPath obj= LocalSystem start= auto displayName= "$displayName"

if ($LASTEXITCODE -ne 0) {
    Write-Host "错误: sc.exe create 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

# 启动服务
Write-Host "正在启动服务..." -ForegroundColor Green
Start-Service $serviceName

# 等待并验证
Start-Sleep -Seconds 3
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') {
    Write-Host "✅ 服务安装成功！CCUService 正在以 LocalSystem 运行。" -ForegroundColor Green
    Write-Host "   现在 EC 读写应该可以正常工作了。"
} else {
    Write-Host "⚠️ 服务已创建但可能未运行，请检查事件查看器。" -ForegroundColor Yellow
    Write-Host "   sc.exe query $serviceName"
}
