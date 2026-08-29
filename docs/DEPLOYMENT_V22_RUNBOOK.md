# CCU 真机部署与验证 Runbook

> **当前运行基线：服务 v25，CLI/WPF v32。**
> v23–v32 已完成分阶段真机验证；当前模式已恢复为 Profile 2。本文保留从旧版本升级到当前基线的流程，以及后续安全验证顺序。
>
> **不会操作的对象**：原厂 `GCUBridge` / `GCUService`、智控中心安装文件、EC 写入、风扇曲线写入、GPU MUX。

## 0. 当前可用内容

- `publish/CCU.Service.v25/` — 当前运行服务
- `publish/CCU.Wpf.v32/` — 当前测试 UI
- `publish/CCU.Cli.v32/` — 当前 CLI
- `publish/CCU-v32-SHA256.txt` — 最新构建校验值
- `publish/update-service-v25.ps1` / `publish/rollback-service-v25.ps1` — 服务部署/回滚脚本
- `tests/smoke.ps1` — 冒烟测试（默认只读）

## 1. 调试窗口要求

- 预计窗口：10–20 分钟
- 暂停游戏、渲染、长时间导出等负载
- 保持原厂智控中心及 `GCUBridge` 正常安装
- 管理员 PowerShell 可用
- 每项控制前记录状态；验证后立即原样恢复

## 2. 部署前只读检查

在项目根目录打开 PowerShell：

```powershell
Get-Service CCUService
sc.exe qc CCUService
Get-CimInstance Win32_Service -Filter "Name='CCUService'" |
  Select-Object Name, State, StartName, PathName
```

预期：

- `CCUService` 当前为 `Running`
- `StartName` 为 `LocalSystem`
- `PathName` 当前基线应为 `publish\CCU.Service.v25\CCU.Service.exe`

核对发布文件哈希：

```powershell
Get-Content publish\CCU-v32-SHA256.txt
Get-FileHash publish\CCU.Service.v25\CCU.Service.exe -Algorithm SHA256
```

## 3. 服务部署原则

部署脚本只用于替换 `CCUService` 的 `ImagePath`，并保留同版本备份/回滚。历史验证通过的流程：

1. 保存当前服务路径到 `%ProgramData%\CCU_Alternative\deployment-backup-vNN.json`
2. 只停止 `CCUService`
3. 将 ImagePath 切换到目标 `CCU.Service.vNN`
4. 启动并等待 `Running`
5. 启动失败则自动切回备份版本并恢复运行

当前服务已在 v25 运行；除非有新服务代码，否则不需要执行部署脚本。

## 4. 第一阶段：只读冒烟（不发控制命令）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\smoke.ps1 -Version v25
```

必须全部通过：

- [ ] `CCUService` 运行
- [ ] `\\.\pipe\CCU.Service.Pipe` 存在
- [ ] `status --json` 返回模式/温度等状态
- [ ] 今日 NLog 文件存在且非空

补充检查：

```powershell
Get-Content "C:\ProgramData\CCU_Alternative\Info\$(Get-Date -Format yyyy-MM-dd).txt" -Tail 80
Get-Content "C:\ProgramData\CCU_Alternative\nlog-internal.log" -Tail 50 -ErrorAction SilentlyContinue
```

重点查找：

- `GCUBridge MQTT 已连接`
- `clientId=PluginClient_18`
- 不应出现认证失败、持续重连或进程崩溃

> 若 `_18` 凭据被 broker 拒绝：先记录日志，不盲试账号；回滚后再分析。

## 5. 第二阶段：最小控制验证（逐项执行）

### 5.1 模式切换

先记录当前模式：

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe status --json
```

只切一次游戏模式，再切回原模式：

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe mode gaming --json
publish\CCU.Cli.v32\CCU.Cli.exe status --json
publish\CCU.Cli.v32\CCU.Cli.exe mode custom --profile 2 --json
publish\CCU.Cli.v32\CCU.Cli.exe status --json
```

验收：CLI 成功、原厂 UI 状态同步、`MainOption.json` 回读一致。模式确认通常约 10–30 秒。

### 5.2 强冷

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe fan-boost on --json
publish\CCU.Cli.v32\CCU.Cli.exe fan-boost off --json
```

验收：风扇响应，状态回读 `fanBoost` 正确。

### 5.3 GPU OC（仅狂暴模式）

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe mode turbo --json
publish\CCU.Cli.v32\CCU.Cli.exe oc on --json
publish\CCU.Cli.v32\CCU.Cli.exe oc off --json
publish\CCU.Cli.v32\CCU.Cli.exe mode custom --profile 2 --json
```

验收：回读 `gpuOcOffset` 为 150 / 0。结束后恢复原模式。

## 6. 第三阶段：灯光验证

从最安全且已验证的静态色开始：

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe rgb single --color 0,212,170 --bright 2 --json
```

再逐个抽查：

```powershell
publish\CCU.Cli.v32\CCU.Cli.exe rgb breathing --color 0,212,170 --bright 2 --speed 2 --json
publish\CCU.Cli.v32\CCU.Cli.exe rgb rainbow --bright 2 --speed 2 --json
publish\CCU.Cli.v32\CCU.Cli.exe rgb off --json
```

验收记录：每个原厂 effect 名是否被接受、速度方向是否正确。未响应的 effect 只记录，不连续重发。

## 7. WPF UI 验证

运行：

```powershell
publish\CCU.Wpf.v32\CCU.Wpf.exe
```

清单：

- [ ] 首页模式卡片和当前模式标签
- [ ] 狂暴静技/极速
- [ ] 强冷和 GPU OC
- [ ] 自定义 Profile 别名/槽位
- [ ] 键盘灯效（单区限制见第 9 节）
- [ ] 风扇曲线只读（失败回退默认也算安全通过，记录日志）
- [ ] 屏幕亮度
- [ ] 刷新率列表（首次只枚举，不切换）
- [ ] 应用绑定：先绑定 `notepad.exe` → 游戏模式，验证进入/离开和 3 秒防抖

## 8. 手动回滚

任何异常可立即运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File publish\rollback-service-v25.ps1
```

回滚只恢复 `CCUService` 的旧 ImagePath 并启动原版本，不修改原厂服务或配置文件。

## 9. 已确认硬件限制与已知问题

- 本机键盘为**单区 RGB**：Wave/Sine/Diagonal 等方向性效果没有分区变化，CLI 保留原生命令但 UI 后续应标记为不支持。
- `HidLightbar_Logo` 独立控制在本机不适用；Logo 跟随键盘灯。后续 UI 应隐藏/标记独立 Logo 控制。
- 模式切换由原厂 GCUBridge 落盘/确认链路完成，通常约 10–30 秒；WPF 现阶段可能在确认前短暂显示旧状态，最终以轮询回读为准。
- WPF 偶发会出现“切换中...”提示未及时清除；真实状态可用 CLI `status --json` 确认，必要时用 CLI 原样恢复。
- 风扇曲线只读目前可能返回 `ACPI WMI not initialized`，属预期内失败项；禁止写入。

## 10. 验证记录模板

| 项目 | 结果 | 日志/备注 |
|---|---|---|
| 服务启动 + NLog | ⬜ | |
| IPC status 小响应 | ⬜ | |
| MQTT `_18` 登录 | ⬜ | |
| 办公/游戏 | ⬜ | |
| 狂暴静技/极速 | ⬜ | |
| 强冷 | ⬜ | |
| GPU OC | ⬜ | |
| 静态 RGB | ⬜ | |
| 其他灯效 | ⬜ | |
| 单区方向效果/Logo 限制 | ⬜ | |
| WPF UI | ⬜ | |
| 应用绑定 | ⬜ | |
| 风扇曲线只读 | ⬜ | |
| 显示控制 | ⬜ | |
| 回滚脚本（仅必要时） | ⬜ | |
