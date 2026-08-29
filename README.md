# CCU — 智控中心替代版 (CCU.Alternative)

> **⚠️ 本项目处于受控真机验证阶段。**
> MQTT-first 核心控制链路已在真机上完成 v23–v32 分阶段验证；当前运行基线为服务 v25、CLI/WPF v32。模式切换、强冷、GPU OC、主要键盘灯效及 IPC 并发/长连接修复均已实测。
> 应用绑定、显示控制和风扇曲线读取仍需后续验证；EC 写入、风扇曲线写入、GPU MUX 保持禁用。

一个面向机械师（MACHENIKE）ID6 系列笔记本的开源控制中心替代方案，目标是逐步替代原厂「智控中心」软件的常用功能。与轻量托盘工具 [id6-mode-tray](https://github.com/BeetMan/id6-mode-tray) 是姊妹项目：Mode Tray 只做模式切换入口，本项目追求完整的替代体验。

---

## 当前状态一览

| 模块 | 代码 | 真机验证 |
|---|---|---|
| 模式切换（办公/游戏/狂暴·静技/狂暴·极速/自定义 Profile） | ✅ 完成 | ✅ 已通过 |
| 一键强冷 / GPU OC +150MHz | ✅ 完成 | ✅ 已通过 |
| 状态监控（温度/占用/风扇转速/当前模式） | ✅ 完成 | ✅ 已通过 |
| 应用绑定自动切换（前台进程 → 自动切模式） | ✅ 完成 | ⏳ 待验证 |
| 键盘 RGB（静态/呼吸/彩虹等） | ✅ 完成 | ✅ 已通过 |
| 键盘 Wave/方向性效果 | ✅ 命令完成 | ⚠️ 本机单区 RGB 不支持 |
| Logo 灯独立控制 | ✅ 命令完成 | ⚠️ 本机不适用（Logo 跟随键盘） |
| 显示控制（亮度/刷新率） | ✅ 完成 | ⏳ 待验证 |
| 风扇曲线只读 | ✅ 完成 | ⚠️ 原厂 ACPI WMI 未初始化 |
| 风扇曲线写入 | 🔒 刻意锁定 | 需 EC 研究支线验证后再开放 |
| GPU MUX 切换 / 设备开关 | 🔒 刻意锁定 | 同上 |
| Named Pipe IPC 并发/长连接 | ✅ 完成 | ✅ 已通过 |

---

## 架构：为什么是 MQTT 优先

原厂方案中，智控中心 UI 通过本机 `GCUBridge` 服务（MQTT，`127.0.0.1:13688`）下发控制命令。这套协议已被姊妹项目 Mode Tray 完整逆向并在真机上长期稳定使用。

因此本项目采取 **MQTT 优先** 路线：

```
CCU.Wpf (用户界面) ─┐
CCU.Cli (命令行)   ─┤─ Named Pipe → CCU.Service ─→ GCUBridge MQTT (原厂服务, 只发送已验证命令)
                    │                    │
                    │                    ├─→ 原厂配置文件 (只读解析状态, 永不写入)
                    │                    └─→ LibreHardwareMonitor (温度/占用, 用户态)
                    │
                    └─ EC/WMI 直写 = 研究支线, 默认全部关闭
```

好处：
- **风险大幅下降**——所有控制命令都是原厂软件自己也在用的已验证命令，不直接碰 EC 寄存器
- **与原厂服务共存**而不是替换它，随时可以回退
- 服务甚至不再强制需要 LocalSystem 权限（仅 EC 诊断需要）

EC 直写路线（`AcpiTest_MULong` WMI 通道、约 120 个寄存器映射）作为研究支线保留在代码中，只开放只读诊断，等全 MQTT 版验证稳定后再逐步研究。

## 组件

```
src/
├── CCU.Service    .NET 8 Worker Service — 核心：MQTT 控制 + 状态聚合 + IPC 服务端
├── CCU.Wpf        WPF 控制界面（性能/GPU/风扇/键盘 RGB/显示/设备/设置）
├── CCU.Cli        命令行接口（含 monitor 流式 JSON 输出，适合 AI Agent 调用）
├── CCU.Shared     IPC 协议 + 模式/绑定模型 + EC 寄存器映射（研究支线）
└── (探针工具)     EcProbe / HwProbe / PipeProbe / MqttProbe / QuickDiag / MinimalPipe
tests/smoke.ps1    冒烟测试（默认只读；-WithModeSwitch 做模式切换往返）
docs/              开发计划与实施记录
```

## 开发基准

- 笔记本：机械师曙光 16S Ultra（Intel Core Ultra 7 255HX + RTX 5070 Ti）
- 原厂软件：智控中心 `5.60.60.17`（Machenike 定制版，UI 包 `CCU.WinUI`）
- 系统：Windows 11 x64，.NET 8
- 其他机型/其他版本智控中心：**未验证**，MQTT 端口、topic、Action 名称、凭据都可能不同

## 构建

```powershell
dotnet build CCU.Alternative.slnx -c Release
```

当前状态：**0 警告 0 错误**（Debug/Release 双配置）。

依赖说明：项目引用本地的 `M2Mqtt.Net.dll`（原厂软件自带的 MQTT 库，SHA1 `690768514CED67987494CA8F6C450AD689BCB68F`）。该 DLL **不入库**，如需本地构建请从你合法安装的智控中心原始文件中自行提取，放置到 `src/MqttProbe/M2Mqtt.Net.dll`。

## 部署（⚠️ 谨慎）

部署会用 CCU.Service 替换/并存原厂 `GCUBridge` 服务体系：

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1     # 发布到 publish/
# 以管理员运行 install-service.ps1                        # 注册 CCUService (LocalSystem)
```

- 服务当前以 LocalSystem 运行（历史原因，EC 诊断需要）；MQTT 控制本身不需要管理员
- 卸载：`uninstall-service.ps1`，不会动原厂软件
- **首次部署前建议先看 `docs/DEVELOPMENT_PLAN.md` 里的风险说明**

## 测试

```powershell
powershell -ExecutionPolicy Bypass -File tests\smoke.ps1                    # 只读检查
powershell -ExecutionPolicy Bypass -File tests\smoke.ps1 -WithModeSwitch   # 含模式切换往返
```

## 安全边界（设计原则，非口号）

1. **EC 写入默认关闭**：性能模式/强冷/OC 等全部走原厂 MQTT 已验证命令；风扇表写入、GPU MUX、设备开关的 IPC 消息会明确返回"研究支线未启用"
2. **原厂配置文件只读**：状态解析只读 `MainOption.json` / `Mode3|4_Profile*.json` / `settings.dat`，永不写入
3. **厂商派生文件不入库**：本仓库不含任何原厂 DLL/EXE/反编译产物；`.gitignore` 白名单策略防误提交
4. **静默失败 forbidden**：服务端异常全部进日志（NLog → `C:\ProgramData\CCU_Alternative\`）

## 已知限制

- 本机键盘为单区 RGB；Wave/Sine/Diagonal 等方向性效果在固件层面无可见分区变化。
- Logo 灯在本机没有独立控制通道，跟随键盘灯；后续 UI 应隐藏或标记该功能。
- 模式切换依赖原厂 GCUBridge 落盘/确认链路，通常约 10–30 秒；WPF 偶发提示未及时清除，真实状态以 CLI `status --json` 回读为准。
- 风扇曲线读取可能返回 `ACPI WMI not initialized`；失败时界面回退默认曲线，写入保持禁用。
- GPU 超频固定 +150/0 MHz 两档（原厂验证值），任意偏移未验证。
- 色彩预设/ICC 管理依赖原厂专有协议，暂缓。

## 路线图

见 [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md)：阶段 0 基建（基本完成）→ 阶段 1 控制链路可靠化（代码完成，待验证）→ 阶段 2 功能补齐（大部分完成）→ 阶段 3 产品化（安装器/电池/多平台适配）。

## 免责声明

本项目与机械师（MACHENIKE）、智控中心开发方（AISTONE GLOBAL (SUZHOU) LIMITED）无隶属、背书或授权关系，是非官方的互操作研究项目。它通过向本机原厂服务发送已验证的控制命令工作，不包含、不分发任何原厂文件或反编译产物。使用产生的功耗、噪音、温度、稳定性变化需自行承担；对设备固件/硬件造成的任何影响（尤其在启用未来 EC 写入功能后）作者不承担责任。
