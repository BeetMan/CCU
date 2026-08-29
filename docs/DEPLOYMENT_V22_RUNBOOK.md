# CCU v22 真机部署与验证 Runbook

> **当前状态：仅准备完成，尚未执行。**
> 本文用于用户明确给出调试窗口后操作。发布构建本身不会修改服务；部署步骤才会短暂停止 `CCUService`。
>
> **不会操作的对象**：原厂 `GCUBridge` / `GCUService`、智控中心安装文件、EC 写入。

## 0. 已准备内容

- `publish/CCU.Service.v22/` — 新服务
- `publish/CCU.Wpf.v22/` — 新 WPF UI
- `publish/CCU.Cli.v22/` — 新 CLI
- `publish/CCU-v22-SHA256.txt` — 关键文件校验值
- `publish/update-service-v22.ps1` — 安全部署（启动失败自动回滚）
- `publish/rollback-service-v22.ps1` — 手动回滚
- `tests/smoke.ps1` — 冒烟测试（默认只读）

## 1. 调试窗口要求

- 预计首次窗口：10–20 分钟
- 暂停游戏、渲染、长时间导出等负载
- 保持原厂智控中心及 `GCUBridge` 正常安装
- 管理员 PowerShell 可用

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
- `PathName` 仍指向 `publish\CCU.Service.v21\CCU.Service.exe`

核对发布文件哈希：

```powershell
Get-Content publish\CCU-v22-SHA256.txt
Get-FileHash publish\CCU.Service.v22\CCU.Service.exe -Algorithm SHA256
```

## 3. 部署 v22（会短暂停止 CCUService）

以管理员 PowerShell 运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File publish\update-service-v22.ps1
```

脚本会：

1. 保存当前服务路径到 `%ProgramData%\CCU_Alternative\deployment-backup-v22.json`
2. 只停止 `CCUService`
3. 将 ImagePath 切换到 `CCU.Service.v22`
4. 启动并等待 `Running`
5. 启动失败则自动切回 v21 并恢复运行

## 4. 第一阶段：只读冒烟（不发控制命令）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\smoke.ps1 -Version v22
```

必须全部通过：

- [ ] `CCUService` 运行
- [ ] `\\.\pipe\CCU.Service.Pipe` 存在
- [ ] `status --json` 返回模式/温度等状态（验证 IPC 小响应修复）
- [ ] 今日 NLog 文件存在且非空（验证日志修复）

补充检查：

```powershell
Get-Content "C:\ProgramData\CCU_Alternative\Info\$(Get-Date -Format yyyy-MM-dd).txt" -Tail 80
Get-Content "C:\ProgramData\CCU_Alternative\nlog-internal.log" -Tail 50 -ErrorAction SilentlyContinue
```

重点查找：

- `GCUBridge MQTT 已连接`
- `clientId=PluginClient_21`
- 不应出现认证失败、持续重连或进程崩溃

> 如果 `_21` 凭据被 broker 拒绝：先记录日志，不盲试账号；回滚后再分析。

## 5. 第二阶段：最小控制验证（逐项执行）

### 5.1 模式切换

先记录当前模式：

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe status --json
```

只切一次游戏模式，再切回原模式：

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe mode gaming --json
publish\CCU.Cli.v22\CCU.Cli.exe status --json
```

验收：CLI 成功、原厂 UI 状态同步、`MainOption.json` 回读一致。

### 5.2 强冷

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe fan-boost on --json
publish\CCU.Cli.v22\CCU.Cli.exe fan-boost off --json
```

验收：风扇响应，状态回读 `fanBoost` 正确。

### 5.3 GPU OC（仅狂暴模式）

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe mode turbo --json
publish\CCU.Cli.v22\CCU.Cli.exe oc on --json
publish\CCU.Cli.v22\CCU.Cli.exe oc off --json
```

验收：回读 `gpuOcOffset` 为 150 / 0。结束后恢复原模式。

## 6. 第三阶段：灯光验证

从最安全且已验证的静态色开始：

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe rgb single --color 0,212,170 --bright 2 --json
```

再逐个抽查：

```powershell
publish\CCU.Cli.v22\CCU.Cli.exe rgb breathing --color 0,212,170 --bright 2 --speed 2 --json
publish\CCU.Cli.v22\CCU.Cli.exe rgb rainbow --bright 2 --speed 2 --json
publish\CCU.Cli.v22\CCU.Cli.exe rgb wave --bright 2 --speed 2 --json
```

验收记录：每个原厂 effect 名是否被接受、速度方向是否正确。未响应的 effect 只记录，不连续重发。

## 7. WPF UI 验证

运行：

```powershell
publish\CCU.Wpf.v22\CCU.Wpf.exe
```

清单：

- [ ] 首页模式卡片和当前模式标签
- [ ] 狂暴静技/极速
- [ ] 强冷和 GPU OC
- [ ] 自定义 Profile 别名/槽位
- [ ] 键盘灯效和 Logo 灯
- [ ] 风扇曲线只读（失败回退默认也算安全通过，记录日志）
- [ ] 屏幕亮度
- [ ] 刷新率列表（首次只枚举，不切换）
- [ ] 应用绑定：先绑定 `notepad.exe` → 游戏模式，验证进入/离开和 3 秒防抖

## 8. 手动回滚

任何异常可立即运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File publish\rollback-service-v22.ps1
```

回滚只恢复 `CCUService` 的旧 ImagePath 并启动原版本，不修改原厂服务或配置文件。

## 9. 验证记录模板

| 项目 | 结果 | 日志/备注 |
|---|---|---|
| 服务启动 + NLog | ⬜ | |
| IPC status 小响应 | ⬜ | |
| MQTT `_21` 登录 | ⬜ | |
| 办公/游戏 | ⬜ | |
| 狂暴静技/极速 | ⬜ | |
| 强冷 | ⬜ | |
| GPU OC | ⬜ | |
| 静态 RGB | ⬜ | |
| 其他灯效 | ⬜ | |
| WPF UI | ⬜ | |
| 应用绑定 | ⬜ | |
| 风扇曲线只读 | ⬜ | |
| 显示控制 | ⬜ | |
| 回滚脚本（仅必要时） | ⬜ | |
