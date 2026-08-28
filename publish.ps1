# CCU Alternative — 发布脚本
# 生成 publish/ 目录下的三个产品组件

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  CCU Alternative — 发布构建" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# 1. CCU.Service
Write-Host "[1/3] 构建 CCU.Service..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Service/CCU.Service.csproj" -c Release -r win-x64 --self-contained true -o "$root/publish/CCU.Service"
Write-Host "  ✅ CCU.Service 完成" -ForegroundColor Green

# 2. CCU.Wpf
Write-Host "[2/3] 构建 CCU.Wpf..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Wpf/CCU.Wpf.csproj" -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o "$root/publish/CCU.Wpf"
Write-Host "  ✅ CCU.Wpf 完成" -ForegroundColor Green

# 3. CCU.Cli
Write-Host "[3/3] 构建 CCU.Cli..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Cli/CCU.Cli.csproj" -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o "$root/publish/CCU.Cli"
Write-Host "  ✅ CCU.Cli 完成" -ForegroundColor Green

# 检查
$total = (Get-ChildItem "$root/publish" -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  发布完成!  总大小: $([math]::Round($total)) MB" -ForegroundColor Green
Write-Host "  输出目录: $root\publish\" -ForegroundColor Green
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# 将安装/卸载/测试脚本复制到 publish 目录
Write-Host "[extra] 复制安装脚本..." -ForegroundColor Yellow
Copy-Item -Force "$root\install-service.ps1" "$root\publish\" -ErrorAction SilentlyContinue
Copy-Item -Force "$root\uninstall-service.ps1" "$root\publish\" -ErrorAction SilentlyContinue
Copy-Item -Force "$root\test-wmi-access.ps1" "$root\publish\" -ErrorAction SilentlyContinue
Write-Host "  ✅ 安装脚本已复制到 publish\" -ForegroundColor Green
