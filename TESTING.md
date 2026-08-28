# CCU Alternative — 真机测试指南

## 环境要求
- 机器: MACHENIKE L16 (或同系机械革命笔记本)
- 系统: Windows 11 x64
- .NET 8 运行时 (如果不用 self-contained) 或下载 publish 目录 (已包含运行时)
- 管理员权限 (PnP 设备操作和 Windows Service 需要)

## 第一步: 硬件诊断 (不需要安装)

```powershell
# 在项目根目录
cd "D:\ID6 Control Center\CCU.Alternative"
dotnet run --project src/CCU.Cli -- hwprobe
```

预期输出: AcpiTest_MULong 可用, CPU/GPU 传感器读数正常, 系统信息正确

## 第二步: 安装 CCU.Service 为 Windows Service (LocalSystem)

**EC 写入需要 SYSTEM 权限。** 必须以 LocalSystem 运行 Service。

```powershell
# 以管理员身份运行 PowerShell
cd "D:\ID6 Control Center\CCU.Alternative"
powershell -NoProfile -ExecutionPolicy Bypass -File install-service.ps1
```

如果还未发布:
```powershell
# 先发布，再安装
powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File install-service.ps1
```

验证服务是否以 LocalSystem 运行:
```powershell
sc.exe qc CCUService
# 应显示: SERVICE_START_NAME : LocalSystem
```

## 第三步: 验证 WMI ACPI 访问

```powershell
# 测试当前用户是否可访问 WMI AcpiTest_MULong
powershell -NoProfile -ExecutionPolicy Bypass -File test-wmi-access.ps1

# 如果直接访问被拒绝，说明需要 SYSTEM — 这正是安装 Service 的原因
```

**工作原理**: CCU.Service 作为 Windows Service 以 LocalSystem (NT AUTHORITY\SYSTEM) 运行，其 WMI 调用可访问 `root\wmi\AcpiTest_MULong.GetSetULong`。WPF UI 通过 Named Pipe (`CCU.Service.Pipe`) 以普通用户身份与 Service 通信。

## 第四步: 通过 CLI 查询服务

```powershell
# 另一个终端 (不需要管理员)
cd "D:\ID6 Control Center\CCU.Alternative"
dotnet run --project src/CCU.Cli -- status --json
```

预期: JSON 响应 (error 表示服务未连接, 则需要检查第二步)

## 第五步: 启动 WPF 前端

```powershell
dotnet run --project src/CCU.Wpf
```

预期: 深色玻璃拟态窗口, 左侧 7 项导航, 切换性能模式触发 OSD 弹出, 系统托盘出现彩色图标

## 第六步: EC 协议验证 (最关键)

在 WPF/CLI 连接服务后, 用 `ccu invoke` 直接测试 EC 读写:

```powershell
# 读 EC 地址 0x04CC (性能模式)
dotnet run --project src/CCU.Cli -- invoke GetHardwareInfo '{}'
```

## 故障排查

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| "无法连接到 CCU 服务" | Service 未启动 | `sc.exe start CCUService` |
| WPF 窗口空白 | 首次加载慢 | 等几秒, 检查任务管理器 |
| 托盘图标不出现 | 无桌面会话 | 必须在实际桌面环境运行 |
| AcpiTest_MULong 不可用 | GCUBridge 服务未运行 | `sc start GCUBridge` |
| PnP 查询为空 | 权限不足 | 以管理员运行 |
| WMI "拒绝访问" | 非 SYSTEM 用户 | Service 应该正在以 LocalSystem 运行，检查 `sc.exe qc CCUService` |
| Service 启动后立即停止 | .NET 未安装或配置错误 | 检查事件查看器 → Windows 日志 → 应用程序 |
| NLog 不输出日志 | 工作目录为 System32 | 已在 Program.cs 中用 AppContext.BaseDirectory 修复 |

## Service 管理命令

```powershell
sc.exe start  CCUService      # 启动
sc.exe stop   CCUService      # 停止
sc.exe qc     CCUService      # 查看配置 (确认 LocalSystem)
sc.exe query  CCUService      # 查看状态

# 卸载
powershell -NoProfile -ExecutionPolicy Bypass -File uninstall-service.ps1
```
