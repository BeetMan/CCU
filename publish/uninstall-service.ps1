$ErrorActionPreference = "Stop"
$serviceName = "CCUService"

if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "错误: 需要管理员权限！" -ForegroundColor Red
    exit 1
}

$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-NOT $svc) {
    Write-Host "服务 $serviceName 不存在。" -ForegroundColor Yellow
    exit 0
}

Write-Host "正在停止 $serviceName..." -ForegroundColor Yellow
Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "正在删除 $serviceName..." -ForegroundColor Yellow
sc.exe delete $serviceName
Write-Host "✅ 服务已卸载。" -ForegroundColor Green
