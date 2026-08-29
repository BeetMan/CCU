# CCU Alternative — 版本化发布脚本
# 生成 publish/CCU.Service.<version>、CCU.Wpf.<version>、CCU.Cli.<version>
# 仅构建并复制文件，不停止/配置/启动任何 Windows 服务。

param(
    [string]$Version = "v22"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$serviceOut = "$root/publish/CCU.Service.$Version"
$wpfOut = "$root/publish/CCU.Wpf.$Version"
$cliOut = "$root/publish/CCU.Cli.$Version"

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  CCU Alternative — 发布构建" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# 避免旧文件残留污染新版本；只删除本次目标目录，不碰任何其他版本。
Remove-Item -Recurse -Force $serviceOut, $wpfOut, $cliOut -ErrorAction SilentlyContinue

# 1. CCU.Service
Write-Host "[1/3] 构建 CCU.Service $Version..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Service/CCU.Service.csproj" -c Release -r win-x64 --self-contained true -o $serviceOut
if ($LASTEXITCODE -ne 0) { throw "CCU.Service publish failed: $LASTEXITCODE" }
Write-Host "  ✅ CCU.Service 完成: $serviceOut" -ForegroundColor Green

# 2. CCU.Wpf
Write-Host "[2/3] 构建 CCU.Wpf $Version..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Wpf/CCU.Wpf.csproj" -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o $wpfOut
if ($LASTEXITCODE -ne 0) { throw "CCU.Wpf publish failed: $LASTEXITCODE" }
Write-Host "  ✅ CCU.Wpf 完成: $wpfOut" -ForegroundColor Green

# 3. CCU.Cli
Write-Host "[3/3] 构建 CCU.Cli $Version..." -ForegroundColor Yellow
dotnet publish "$root/src/CCU.Cli/CCU.Cli.csproj" -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o $cliOut
if ($LASTEXITCODE -ne 0) { throw "CCU.Cli publish failed: $LASTEXITCODE" }
Write-Host "  ✅ CCU.Cli 完成: $cliOut" -ForegroundColor Green

# 检查关键产物并生成 SHA256 清单
$primaryFiles = @(
    (Join-Path $serviceOut 'CCU.Service.exe'),
    (Join-Path $serviceOut 'NLog.config'),
    (Join-Path $serviceOut 'M2Mqtt.Net.dll'),
    (Join-Path $wpfOut 'CCU.Wpf.exe'),
    (Join-Path $cliOut 'CCU.Cli.exe')
)
foreach ($file in $primaryFiles) {
    if (-not (Test-Path $file)) { throw "发布产物缺失: $file" }
}

$manifestPath = "$root/publish/CCU-$Version-SHA256.txt"
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $manifestLines = foreach ($file in $primaryFiles) {
        $stream = [System.IO.File]::OpenRead($file)
        try {
            $hash = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
            "$hash  $($file.Substring($root.Length + 1))"
        }
        finally { $stream.Dispose() }
    }
    $manifestLines | Set-Content -Encoding ASCII $manifestPath
}
finally { $sha256.Dispose() }
Write-Host "  ✅ SHA256 清单: $manifestPath" -ForegroundColor Green

# 检查本版本大小
$total = ((Get-ChildItem $serviceOut, $wpfOut, $cliOut -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  发布完成!  总大小: $([math]::Round($total)) MB" -ForegroundColor Green
Write-Host "  输出目录: publish\*.$Version" -ForegroundColor Green
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# 部署脚本维护在 publish/ 中；此处不复制根目录脚本，避免覆盖版本化部署工具。
Write-Host "[extra] 部署脚本保持不变（未执行）" -ForegroundColor DarkGray
