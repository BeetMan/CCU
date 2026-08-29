# CCUService v22 安全部署脚本
# 需要管理员权限。执行时会备份当前服务路径；v22 启动失败则自动回滚。
# 本脚本不会停止/修改原厂 GCUBridge/GCUService，只操作 CCUService。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'CCUService'
$targetExe = Join-Path $PSScriptRoot 'CCU.Service.v22\CCU.Service.exe'
$backupDir = Join-Path $env:ProgramData 'CCU_Alternative'
$backupFile = Join-Path $backupDir 'deployment-backup-v22.json'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '请以管理员身份运行此脚本。'
    }
}

function Set-ServiceBinaryPath([string]$path) {
    $output = & sc.exe config $serviceName 'binPath=' "`"$path`"" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config 失败 ($LASTEXITCODE): $output" }
}

function Wait-ServiceState([string]$state, [int]$timeoutSeconds = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        $svc = Get-Service $serviceName -ErrorAction Stop
        if ($svc.Status.ToString() -eq $state) { return }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw "等待 $serviceName 进入 $state 超时。当前状态: $($svc.Status)"
}

Assert-Administrator
if (-not (Test-Path $targetExe)) { throw "未找到 v22 服务文件: $targetExe" }

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
if (-not $service) { throw "服务 $serviceName 不存在，请先运行 install-service.ps1。" }

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$backup = [ordered]@{
    ServiceName = $serviceName
    PreviousPath = $service.PathName
    PreviousStartMode = $service.StartMode
    CapturedAt = (Get-Date).ToString('o')
    TargetPath = $targetExe
}
$backup | ConvertTo-Json | Set-Content -Encoding UTF8 $backupFile
Write-Host "✓ 已备份当前服务配置: $backupFile" -ForegroundColor Green
Write-Host "  当前路径: $($service.PathName)" -ForegroundColor DarkGray
Write-Host "  目标路径: $targetExe" -ForegroundColor DarkGray

try {
    Write-Host '[1/3] 停止 CCUService...' -ForegroundColor Yellow
    Stop-Service $serviceName -Force -ErrorAction Stop
    Wait-ServiceState 'Stopped'

    Write-Host '[2/3] 切换到 v22...' -ForegroundColor Yellow
    Set-ServiceBinaryPath $targetExe

    Write-Host '[3/3] 启动 v22...' -ForegroundColor Yellow
    Start-Service $serviceName -ErrorAction Stop
    Wait-ServiceState 'Running' 20

    $current = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if ($current.PathName -notlike "*$targetExe*") {
        throw "服务已启动但 ImagePath 校验失败: $($current.PathName)"
    }

    Write-Host ''
    Write-Host '✓ CCUService v22 已启动。下一步只运行只读冒烟测试：' -ForegroundColor Green
    Write-Host '  powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -Version v22' -ForegroundColor Cyan
}
catch {
    Write-Host "✗ v22 部署失败: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '正在自动回滚...' -ForegroundColor Yellow
    try { Stop-Service $serviceName -Force -ErrorAction SilentlyContinue } catch { }
    Set-ServiceBinaryPath $backup.PreviousPath
    Start-Service $serviceName
    Wait-ServiceState 'Running' 20
    Write-Host "✓ 已回滚并启动原版本: $($backup.PreviousPath)" -ForegroundColor Green
    throw
}
