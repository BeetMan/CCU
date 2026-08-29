# CCU 冒烟测试 — 需要新版服务 (v22+) 部署后运行
# 用法: powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 [-WithModeSwitch]
# 默认只跑只读检查; -WithModeSwitch 额外做一轮模式切换往返 (需要用户确认空闲)

param(
    [switch]$WithModeSwitch,
    [string]$Version = "v22"
)

$ErrorActionPreference = 'Stop'
$cli = "publish\CCU.Cli.$Version\CCU.Cli.exe"
if (-not (Test-Path $cli)) { $cli = "src\CCU.Cli\bin\Debug\net8.0-windows\CCU.Cli.exe" }
if (-not (Test-Path $cli)) { $cli = "src\CCU.Cli\bin\Release\net8.0-windows\CCU.Cli.exe" }
if (-not (Test-Path $cli)) { Write-Host "✗ 未找到已发布或已构建的 CCU.Cli" -ForegroundColor Red; exit 1 }
Write-Host "CLI: $cli" -ForegroundColor DarkGray

$fail = 0
function Check($name, $scriptBlock) {
    try {
        $result = & $scriptBlock
        if ($result) { Write-Host "✓ $name" -ForegroundColor Green; return $true }
        Write-Host "✗ $name" -ForegroundColor Red; $script:fail++
    } catch { Write-Host "✗ $name — $($_.Exception.Message)" -ForegroundColor Red; $script:fail++ }
    return $false
}

Write-Host "=== CCU 冒烟测试 ===" -ForegroundColor Cyan

# 1. 服务在跑
Check "CCUService 正在运行" { (Get-Service CCUService -ErrorAction Stop).Status -eq 'Running' }

# 2. IPC 管道存在
Check "IPC 管道存在" {
    [System.IO.Directory]::GetFiles("\\.\pipe\") -contains "\\.\pipe\CCU.Service.Pipe"
}

# 3. status 拿到真实状态 (验证小响应修复 + MQTT/配置读取)
Check "status 返回模式状态" {
    $out = & $cli status --json 2>&1 | ConvertFrom-Json
    $null -ne $out.operatingMode -and $out.operatingMode -ge 0 -and $out.mode.Length -gt 0
}

# 4. 日志在写 (验证 NLog 修复)
Check "服务日志今天有输出" {
    $log = "C:\ProgramData\CCU_Alternative\Info\$(Get-Date -Format yyyy-MM-dd).txt"
    (Test-Path $log) -and ((Get-Item $log).Length -gt 0)
}

if ($WithModeSwitch) {
    Write-Host "--- 模式切换往返 (请确认机器空闲) ---" -ForegroundColor Yellow
    $original = & $cli status --json | ConvertFrom-Json
    Write-Host "当前: $($original.mode)"

    Check "切到游戏模式" {
        $result = (& $cli mode gaming --json 2>&1) | ConvertFrom-Json
        $result.success -eq $true
    }
    Start-Sleep 1
    Check "切回原模式" {
        $back = @('office','gaming','turbo')[$original.operatingMode]
        if (-not $back) { throw "当前模式 $($original.operatingMode) 无自动回退映射，请手动恢复" }
        $result = (& $cli mode $back --json 2>&1) | ConvertFrom-Json
        $result.success -eq $true
    }
    Start-Sleep 1
    $final = (& $cli status --json 2>&1) | ConvertFrom-Json
    Check "状态回读一致" { $final.operatingMode -eq $original.operatingMode }
}

Write-Host ""
if ($fail -eq 0) { Write-Host "全部通过 ✓" -ForegroundColor Green; exit 0 }
Write-Host "$fail 项失败" -ForegroundColor Red; exit 1
