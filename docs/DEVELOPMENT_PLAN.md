# CCU.Alternative 开发计划

> 接管时间：2026-08-28 · 基于全面状态诊断制定
> 设备基准：机械师曙光 16S Ultra（Intel Ultra 7 255HX + RTX 5070 Ti），智控中心 5.60.60.17

---

## 0. 背景与现状（诊断结论）

### 已验证可用 ✅
- WMI `AcpiTest_MULong` 通道（LocalSystem 下 10 实例，EC 读写实测成功）
- EC 寄存器映射（~120 个地址，`EcRegisterMap.cs`）
- 性能模式 / 风扇 / GPU MUX / 设备开关的代码路径（未经端到端验证）
- 灯光线：OpenRGB 插件 + MQTT 桥（键盘 + Logo 双区，已部署）
- Mode Tray（C 盘，独立发布，不依赖本项目）

### 发现的缺陷 🔴
| # | 问题 | 影响 | 严重度 |
|---|------|------|--------|
| B1 | `PipeServer.HandleConnection` 写完响应立即 `Disconnect()`，内核丢弃客户端未读走的缓冲数据 → **小响应（<1KB 左右）必然丢失**，大响应侥幸存活 | CLI 所有命令、WPF 全部控制功能失效，只剩"服务无响应" | **致命** |
| B2 | `HandleConnection` 整体 `catch {}` 静默吞异常 | 服务端出错无任何痕迹 | 高 |
| B3 | NLog 自 v21 起零输出（手动 File 日志正常） | 线上不可观测 | 高 |
| B4 | 每次开机服务崩溃重启 2 次才稳定（SCM 恢复策略掩盖） | 启动窗口期不可用、掩盖真实缺陷 | 中 |
| B5 | `CCU.Service` 无版本控制（git 不存在） | 两个月代码无法回溯 | 高 |
| B6 | WPF 发布停留在 6/28，落后于 v21 服务 | UI 与服务协议可能脱节 | 中 |
| B7 | `status` 只回 "request sent"，不回显模式/转速 | 状态查询名不副实 | 低 |

### 已知阻塞 ⛔
- **GPU 超频**：需厂商 NWAPI（GPUInfoDLL.dll）native 接口，未逆向
- **键盘直控**：HID report 协议未知，需 USB 抓包（当前灯光走 MQTT 已可用，暂无必要）
- **内核驱动直写 EC**：Secure Boot 无法关闭，UWACPIDriver.sys 路线冻结（WMI 路线已够用）

### 战略决策点（需在阶段 1 前定案）
**D-1：控制命令走 EC 直写还是 MQTT 转发？**
原厂 GCUService 的 MQTT（127.0.0.1:13688）协议已被 Mode Tray 完整逆向且稳定。

> ✅ **已定案（2026-08-28，用户确认）：全 MQTT 版本先行。**
> 控制命令全部走 MQTT；状态从原厂配置文件只读解析；EC/WMI 降级为只读诊断研究支线，
> IPC 路由默认拒绝所有 EC 写入。全 MQTT 版可日常替代原厂后，再回头研究 EC 底层。
> 风扇曲线写入 (SMAPCTABLE) 等高风险操作推迟到用户明确同意后。
>
> **实施记录（同日）**：新增 `VendorStateReader`（配置只读解析）+ `VendorMqttControl`
> （常驻 MQTT 连接、独立 clientId `_21` 避免与 Mode Tray `_19` 互踢）；
> IPC 新增 SetFanBoost/SetTurboOc；SetFanTable/SetGpuMode/SetDeviceSwitch 返回"未启用"；
> PipeServer 竞态 bug 修复 (WaitForPipeDrain) + 异常不再静默；
> CLI status 显示真实模式状态，新增 fan-boost/oc 命令；NLog 配置简化加固。
> 已编译通过 (0 错误 0 新警告)，**未部署未验证**（待用户给出调试窗口）。
>
> **功能开发记录（同日，第二抨）**：
> - **应用绑定自动切换 (2.2)**：`ForegroundAppMonitor`（前台进程轮询）+
>   `AppProfileStore`（app-profiles.json 持久化）+ 主循环状态机
>   （3s 冷却防抖、仅进不重复写、离开恢复办公模式可配）；
>   IPC 新增 GetModeCatalog/SetAppBindingEnabled，SaveAppProfile/DeleteAppProfile 接通；
> - **WPF 对齐**：状态栏显示真实模式标签；性能页新增狂暴静技/极速细分芯片、
>   自定义 Profile 芯片（服务目录发现）、强冷/OC 快捷开关；
>   设置页新增应用绑定管理（列表/添加/删除/总开关）；
> - 新增 `tests/smoke.ps1` 冒烟脚本（只读默认，-WithModeSwitch 可选）。
> 编译 0 错误 0 新警告，未部署。
>
> **功能开发记录（同日，第三抨：剩余功能全部实现）**：
> - **键盘 RGB (MQTT)**：`VendorLightingController` — 22 种灯效（词表来自反编译
>   RGBKB_Effect 枚举），静态色命令用已验证结构，亮度 0-4 级/速度 1-5；
>   Logo 灯 (HidLightbar_Logo/Ctrl) 独立开关，跟随键盘同色；
> - **风扇曲线只读**：GetFanCurve IPC → SMAPCTABLE cmd 12 尽力读取，
>   失败回退默认曲线；写入仍属研究支线未开放；
> - **显示设置真实化**：亮度走标准 WMI WmiSetBrightness（用户会话），
>   刷新率走 EnumDisplaySettings/ChangeDisplaySettings（异常自动还原）；
>   色彩预设/ICC 依赖原厂专有协议，明确标注缓办；
> - **监控补全**：HardwareInfo 增加风扇转速 (LHM)；
> - CLI rgb 命令对齐新协议词表；display 命令改为直控亮度。
> 编译 0 错误，非 CA1416 警告均为历史遗留。未部署未验证。
>
> **真机验证更新（2026-08-29）**：v23–v32 已完成 MQTT-first 分阶段验证。当前运行基线为服务 v25、CLI/WPF v32；模式/强冷/GPU OC/主要键盘灯效/IPC 并发长连接均实测通过。本机单区 RGB 不支持方向性效果，Logo 无独立控制通道；当前模式已恢复 Profile 2。

---

## 1. 阶段规划

### 阶段 0：抢救 + 基建（目标：1 天，做完才能谈功能）

| 任务 | 内容 | 验收 |
|---|---|---|
| 0.1 | **建 git 仓库**（`git init` + 保守 .gitignore：排除 `publish/`、`bin/`、`obj/`、`*.pdb`、厂商 DLL；`M2Mqtt.Net.dll` 等厂商文件一律不入库） | 首个 commit 完成，`git status` 干净 |
| 0.2 | **修 B1**：响应写完后 `stream.WaitForPipeDrain()` 再 `Disconnect()`；顺手给 pipe 读写加 `pipe.ReadTimeout/WriteTimeout` | 本地 PipeTest 复现用例：小/大响应均完整到达 |
| 0.3 | **修 B2**：`HandleConnection` 异常记入日志（不吞），保留连接级容错 | 人为构造坏消息，日志可见原因且服务不死 |
| 0.4 | **发布 v22**（`publish.ps1` + `update-service-v22.bat`），真机重启服务 | `CCU.Cli status/mode/fan` 全部拿到真实响应 |
| 0.5 | **修 B3**：诊断 NLog（internal log 开关 + 验证 config 加载），至少 Info 级落盘 | 重启服务后 `C:\ProgramData\CCU_Alternative\Info\<date>.txt` 出现启动日志 |
| 0.6 | **查 B4**：开机崩溃两次的根因（怀疑启动时与 GCUService/HwMonitor 初始化竞态），加启动重试或延迟初始化 | 重启系统一次，`wmi_init_log` 只出现 1 次初始化 |

**阶段 0 完成标志**：`CCU.Cli status` 显示真实硬件状态；服务日志连续可查；所有改动进 git。

---

### 阶段 1：核心控制链路可靠化（目标：1 周）

原则：**每条写命令先读后写再回读校验，所有验证过的写地址记录在案。**

| 任务 | 内容 | 验收 |
|---|---|---|
| 1.1 | **GetHardwareInfo 补全**：返回体加 当前性能模式、CPU/GPU 转速、风扇 duty（EC 0x04CC / 0x07C8 / 0x07C9 等已映射地址） | `status` 一条命令回显全部状态 |
| 1.2 | **性能模式切换端到端验证**：office/gaming/turbo（含 silent/extreme 细分，对照 Mode Tray 的 MQTT 行为） | 每种模式：EC 回读值正确 + 原厂 UI 显示一致 |
| 1.3 | **GPU OC 开关**：对照 Mode Tray 已验证的 MQTT `+150MHz/0` 命令，先走 MQTT 转发实现（风险最低），EC 路线作研究项 | CLI `oc on/off` 生效且可回读 |
| 1.4 | **强冷开关**：同上，对照 Mode Tray `Fan/Control` topic | CLI 生效 |
| 1.5 | **风扇曲线读验证**：`FanControlManager` 读 SMAPCTABLE（cmd 12）先跑通，写（cmd 13）暂缓 | CLI 能画出当前 CPU/GPU 曲线 |
| 1.6 | **WPF 升级对齐 v22**：重新发布 CCU.Wpf，修 6/28 遗留的绑定问题清单（memory 中 6/29 未完成项） | UI 实时显示温度/转速/模式，切换按钮全部生效 |
| 1.7 | **自动化冒烟测试**：`tests/smoke.ps1` — 依次调用全部只读命令 + 一轮模式切换往返（office→gaming→office），断言回读值 | 一键跑通，接入每次发布前 |

**阶段 1 完成标志**：WPF 替代品可日常使用（模式/强冷/OC/状态监控），等于 Mode Tray 的超集。

---

### 阶段 2：功能补齐（目标：2 周，可并行拆分）

| 任务 | 内容 | 风险控制 |
|---|---|---|
| 2.1 | **风扇曲线写 + 编辑器**：SMAPCTABLE 写验证（先备份原曲线→写→回读→异常自动还原）→ WPF `FanCurveCanvas`（6/29 被删的类按新架构重写） | 只在"自定义/狂暴"模式允许写；写入前强制备份；提供"恢复默认"按钮 |
| 2.2 | **应用绑定自动切换**：主循环 TODO 实现 —— 前台窗口检测 → 进程名匹配 profile → 自动切模式；去重防抖（同模式不重复写）、游戏全屏检测 | 默认关闭，设置页显式开启；日志记录每次自动切换原因 |
| 2.3 | **显示控制**：亮度/色温/刷新率，先 WMI 路线探测，不通再评估 DDC/CI | 刷新率切换需列出可用值防黑屏 |
| 2.4 | **OSD**：`OsdWindow` 完善，游戏内温度/帧率悬浮（可选） | 纯展示，不写任何硬件 |
| 2.5 | **profile 体系统一**：把 Mode Tray 的 `profiles.json` 经验合入（自定义槽位发现、settings.dat 别名读取），CLI/WPF/自动切换共用一套 | 单一实现，三端复用 |

**阶段 2 完成标志**：替代品覆盖原厂 UI 高频功能 ≥80%，风扇曲线和自动切换是差异化亮点。

---

### 阶段 3：攻坚与产品化（长期）

| 任务 | 说明 |
|---|---|
| 3.1 | **GPU 超频 EC/NWAPI 路线**：逆向 GPUInfoDLL.dll 导出函数（现成 IL 已 dump 在 C 盘工作区，native 部分需 IDA/动态分析）；或确认 MQTT OC 命令足够则关闭此项 |
| 3.2 | **服务健壮化**：心跳/看门狗、EC 通讯失败自动降级只读、启动依赖排序（与 GCUService 共存声明） |
| 3.3 | **安装器**：合并 install-service + WPF 快捷方式 + 运行时检测为一个 MSIX 或 Inno Setup 包 |
| 3.4 | **电源/电池页**：充电阈值、电池保养（EC 地址探测） |
| 3.5 | **多平台适配调研**：WMI 方法名/EC 地址按厂商家族参数化（对照 GCUService IL 中其他机型分支） |
| 3.6 | **发布 C 盘 Mode Tray 与本项目的关系整理**：README 互相引用，Mode Tray 作为轻量入口保留 |

---

## 2. 工程规范（接管即生效）

1. **版本规范**：服务 `v<nn>` 递增（v22 起）；每次发布 = git tag + `update-service-v<nn>.bat`；publish 目录只保留最近 3 版
2. **EC 写安全红线**：
   - 只允许写入 `EcRegisterMap.cs` 中标注 `[Verified]` 的地址
   - 任何写操作：读→写→回读校验→失败告警，曲线类写入必须先备份
   - 破坏性实验只允许在 `EcProbe` 单独工具里做，Service 代码保持保守
3. **日志红线**：服务端禁止静默 catch；关键路径（IPC、EC、模式切换）必须有 Info/Error 日志
4. **厂商文件红线**：延续 C 盘 .gitignore 策略，厂商 DLL/IL/资源一律不入库、不二次分发
5. **验证纪律**：每阶段验收项跑一遍 `tests/smoke.ps1`，真机验证结果记入 `TESTING.md` 追加式日志

## 3. 里程碑一览

```
0 抢救基建 ──► 1 控制链路可靠化 ──► 2 功能补齐 ──► 3 产品化
   1天             1周                  2周          长期
        git+v22        日常可用超集       风扇曲线+自动切换   安装器/多平台
```

## 4. 待用户确认的决策

- [x] D-1 架构路线：已确认全 MQTT 先行；EC/WMI 保持只读诊断。
- [x] 阶段 0/1 部署窗口：v23–v25 服务已部署并验证，当前服务基线 v25。
- [ ] 阶段 2.1 风扇曲线写入涉及 SMAPCTABLE（比单寄存器风险高），需另行确认后才允许验证。
- [x] D 盘仓库已推 GitHub 私有远程：`BeetMan/CCU`。
