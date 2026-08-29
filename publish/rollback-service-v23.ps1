# CCUService v23 手动回滚脚本
# 从 ProgramData\CCU_Alternative\deployment-backup-v23.json 恢复部署前路径。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'CCUService'
$backupFile = Join-Path $env:ProgramData 'CCU_Alternative\deployment-backup-v23.json'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请以管理员身份运行此脚本。'
}
if (-not (Test-Path $backupFile)) { throw "未找到部署备份: $backupFile" }

$backup = Get-Content -Raw $backupFile | ConvertFrom-Json
if (-not $backup.PreviousPath) { throw '备份文件中缺少 PreviousPath。' }

Write-Host "回滚目标: $($backup.PreviousPath)" -ForegroundColor Yellow
Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$output = & sc.exe config $serviceName 'binPath=' "`"$($backup.PreviousPath)`"" 2>&1
if ($LASTEXITCODE -ne 0) { throw "sc.exe config 失败 ($LASTEXITCODE): $output" }

Start-Service $serviceName
$svc = Get-Service $serviceName
$svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
Write-Host '✓ 原版本已恢复并启动。' -ForegroundColor Green
