using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CCU.Shared.IPC;
using CCU.Shared.Models;
using CCU.Service.Core;
using CCU.Service.Infrastructure;
using System.Text.Json;

namespace CCU.Service;

/// <summary>
/// CCU 后台服务 — MQTT 优先架构
///
/// 控制命令（模式/OC/强冷/灯光）走原厂 GCUBridge MQTT (127.0.0.1:13688)；
/// 状态从原厂配置文件只读解析；EC/WMI 仅保留只读诊断（研究支线，写入默认关闭）。
/// </summary>
public class GcuBackgroundService : BackgroundService
{
    private readonly ILogger<GcuBackgroundService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly HardwareMonitorService _hwMonitor;
    private readonly VendorMqttControl _mqtt;
    private readonly VendorStateReader _stateReader;
    private readonly AppProfileStore _appProfiles;
    private readonly WmiAcpiClient _acpi;
    private readonly KernelAcpiClient _kernelAcpi;
    private readonly PipeServer _pipeServer;

    // 应用绑定自动切换状态机
    private string? _lastForegroundProcess;
    private VendorModeDefinition? _autoAppliedMode;
    private DateTime _lastAutoSwitchAt = DateTime.MinValue;
    private static readonly TimeSpan AutoSwitchCooldown = TimeSpan.FromSeconds(3);

    public GcuBackgroundService(
        ILogger<GcuBackgroundService> logger,
        IHostApplicationLifetime appLifetime,
        HardwareMonitorService hwMonitor,
        VendorMqttControl mqtt,
        VendorStateReader stateReader,
        AppProfileStore appProfiles,
        WmiAcpiClient acpi,
        KernelAcpiClient kernelAcpi)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _hwMonitor = hwMonitor;
        _mqtt = mqtt;
        _stateReader = stateReader;
        _appProfiles = appProfiles;
        _acpi = acpi;
        _kernelAcpi = kernelAcpi;

        _pipeServer = new PipeServer("CCU.Service.Pipe", HandleIpcMessage,
            log: message => _logger.LogWarning("{Message}", message));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CCU Service starting (MQTT-first)...");
        _logger.LogInformation("Vendor install dir: {Dir}", _stateReader.MainOptionPath is null
            ? "<未找到智控中心>" : Path.GetDirectoryName(_stateReader.MainOptionPath));

        // 硬件监控（温度/占用，用户态尽力而为）与 MQTT 通道
        try
        {
            _hwMonitor.Initialize();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "硬件监控初始化失败（温度/占用将不可用，MQTT 控制不受影响）");
        }

        try
        {
            _mqtt.EnsureConnected();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GCUBridge MQTT 初始连接失败，将在处理命令时重试");
        }

        // 启动 IPC 管道
        _pipeServer.Start();
        _logger.LogInformation("IPC pipe server started");

        // 主循环 — MQTT 保活 + 硬件监控刷新
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqtt.IsConnected)
                {
                    try { _mqtt.EnsureConnected(); }
                    catch (Exception ex) { _logger.LogDebug(ex, "MQTT 重连待重试"); }
                }

                try { _hwMonitor.Update(); }
                catch (Exception ex) { _logger.LogTrace(ex, "硬件监控刷新失败"); }

                ProcessAppBinding();

                // TODO (阶段3): 前台窗口检测 → 显示设置联动

                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main loop");
            }
        }

        _logger.LogInformation("CCU Service stopped");
    }

    private async Task<IpcMessage> HandleIpcMessage(IpcMessage message)
    {
        try
        {
            _logger.LogTrace("Received IPC: {Type}", message.Type);

            return message.Type switch
            {
                IpcMessageType.GetHardwareInfo => HandleGetHardwareInfo(),
                IpcMessageType.SetPerformanceMode => HandleSetPerformanceMode(message),
                IpcMessageType.SetFanBoost => HandleSetFanBoost(message),
                IpcMessageType.SetTurboOc => HandleSetTurboOc(message),
                IpcMessageType.GetModeCatalog => HandleGetModeCatalog(),
                IpcMessageType.SetAppBindingEnabled => HandleSetAppBindingEnabled(message),
                IpcMessageType.SaveAppProfile => HandleSaveAppProfile(message),
                IpcMessageType.DeleteAppProfile => HandleDeleteAppProfile(message),
                IpcMessageType.EcDiagnostic => HandleEcDiagnostic(),
                IpcMessageType.KernelEcDiagnostic => HandleKernelEcDiagnostic(),

                // === 研究支线（EC 写入），MQTT 优先阶段默认关闭 ===
                IpcMessageType.SetFanTable or IpcMessageType.SetGpuMode or IpcMessageType.SetDeviceSwitch
                    => IpcMessage.Create(IpcMessageType.Error, new
                    {
                        Message = "该功能属于 EC 研究支线，MQTT 优先阶段未启用（见 docs/DEVELOPMENT_PLAN.md）"
                    }),

                _ => IpcMessage.Create(IpcMessageType.Ack, new { Success = false, Error = "Unknown message type" })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling IPC message {Type}", message.Type);
            return IpcMessage.Create(IpcMessageType.Error, new { Message = ex.Message });
        }
    }

    private IpcMessage HandleGetHardwareInfo()
    {
        float? CpuTemp() { try { return _hwMonitor.GetCpuTemperature(); } catch { return null; } }
        float? GpuTemp() { try { return _hwMonitor.GetGpuTemperature(); } catch { return null; } }
        float? CpuUsage() { try { return _hwMonitor.GetCpuUsage(); } catch { return null; } }
        float? GpuUsage() { try { return _hwMonitor.GetGpuUsage(); } catch { return null; } }

        var info = new HardwareInfo
        {
            CpuTemperature = CpuTemp() ?? 0,
            GpuTemperature = GpuTemp() ?? 0,
            CpuUsage = CpuUsage() ?? 0,
            GpuUsage = GpuUsage() ?? 0,
            Timestamp = DateTime.UtcNow,
        };

        // 原厂模式状态（配置文件只读解析，不依赖硬件监控）
        var mode = _stateReader.ReadModeState();
        info.OperatingMode = mode.OperatingMode;
        info.CustomProfileIndex = mode.CustomProfileIndex;
        info.TurboGpuOcOffset = mode.TurboGpuOcOffset;
        info.FanBoostEnabled = mode.FanBoostEnabled;
        info.ModeLabel = DescribeMode(mode);

        return IpcMessage.Create(IpcMessageType.HardwareInfoUpdate, info);
    }

    private string DescribeMode(VendorModeState mode)
    {
        try
        {
            return mode.OperatingMode switch
            {
                0 => "办公模式",
                1 => "游戏模式",
                2 => mode.TurboSilent == 1 ? "狂暴 · 静技" : "狂暴 · 极速",
                3 => _stateReader.BuildModeCatalog()
                        .FirstOrDefault(m => m.OperatingMode == 3 && m.ProfileIndex == mode.CustomProfileIndex)
                        ?.Label ?? $"自定义 Profile {mode.CustomProfileIndex + 1}",
                _ => "未知"
            };
        }
        catch
        {
            return "未知";
        }
    }

    private IpcMessage HandleSetPerformanceMode(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetModeRequest>();
        if (req == null) return Error("Invalid payload");

        var catalog = _stateReader.BuildModeCatalog();
        VendorModeDefinition? target = req.Mode switch
        {
            0 or 1 => catalog.FirstOrDefault(m => m.OperatingMode == req.Mode),
            2 => req.Silent is int silent && silent == 1
                ? catalog.FirstOrDefault(m => m.OperatingMode == 2 && m.Silent == 1)
                : catalog.FirstOrDefault(m => m.OperatingMode == 2 && m.Extreme == 1),
            3 => catalog.FirstOrDefault(m =>
                    m.OperatingMode == 3 && m.ProfileIndex == (req.ProfileIndex ?? 0))
                ?? throw new InvalidOperationException(
                    $"自定义 Profile {(req.ProfileIndex ?? 0) + 1} 未启用或不存在"),
            _ => null
        };

        if (target is null)
        {
            return Error($"找不到模式 {req.Mode} 对应的定义（智控中心未安装或配置不可读）");
        }

        _mqtt.SwitchMode(target);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, Mode = target.Label });
    }

    private IpcMessage HandleSetFanBoost(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetFanBoostRequest>();
        if (req == null) return Error("Invalid payload");
        _mqtt.SetFanBoost(req.Enable);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, Enable = req.Enable });
    }

    private IpcMessage HandleSetTurboOc(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetTurboOcRequest>();
        if (req == null) return Error("Invalid payload");
        _mqtt.SetTurboOc(req.Offset);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, Offset = req.Offset });
    }

    private IpcMessage Error(string msg) =>
        IpcMessage.Create(IpcMessageType.Error, new { Message = msg });

    // ========================
    // 应用绑定自动切换
    // ========================

    private void ProcessAppBinding()
    {
        try
        {
            var settings = _appProfiles.Current;
            if (!settings.Enabled) { _autoAppliedMode = null; return; }

            var foreground = ForegroundAppMonitor.GetForegroundProcessName();
            if (foreground == _lastForegroundProcess)
            {
                // 前台未变化：若已处于自动应用的模式但状态被外部改变，放弃跟踪
                return;
            }
            _lastForegroundProcess = foreground;

            var binding = _appProfiles.FindBinding(foreground);
            var modeState = _stateReader.ReadModeState();

            if (binding is not null)
            {
                var target = ResolveBindingTarget(binding, modeState);
                if (target is null) return;

                // 已在目标模式则不重复写
                if (target.Matches(modeState)) { _autoAppliedMode = target; return; }
                if (DateTime.UtcNow - _lastAutoSwitchAt < AutoSwitchCooldown) return;

                _logger.LogInformation("应用绑定: 前台 {Process} → 自动切换到 {Label}",
                    foreground, target.Label);
                _mqtt.SwitchMode(target);
                _autoAppliedMode = target;
                _lastAutoSwitchAt = DateTime.UtcNow;
            }
            else if (_autoAppliedMode is not null && settings.RestoreOnLeave)
            {
                // 离开绑定应用：恢复办公模式（仅在当前还处于自动应用的模式时）
                var restore = new VendorModeDefinition("办公模式(自动恢复)", "OPERATING_OFFICE_MODE", 0, 0);
                if (!restore.Matches(modeState) &&
                    DateTime.UtcNow - _lastAutoSwitchAt >= AutoSwitchCooldown)
                {
                    _logger.LogInformation("应用绑定: 离开 {Process} → 恢复办公模式", _lastForegroundProcess);
                    _mqtt.SwitchMode(restore);
                    _lastAutoSwitchAt = DateTime.UtcNow;
                }
                _autoAppliedMode = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "应用绑定切换失败");
        }
    }

    private VendorModeDefinition? ResolveBindingTarget(AppProfile binding, VendorModeState currentState)
    {
        return binding.Mode switch
        {
            0 => new VendorModeDefinition(binding.DisplayName, "OPERATING_OFFICE_MODE", 0, 0),
            1 => new VendorModeDefinition(binding.DisplayName, "OPERATING_GAMING_MODE", 1, 0),
            2 => binding.Silent == 1
                ? new VendorModeDefinition(binding.DisplayName, "OPERATING_TURBO_MODE", 2, 0, Silent: 1, Extreme: 0)
                : new VendorModeDefinition(binding.DisplayName, "OPERATING_TURBO_MODE", 2, 0, Silent: 0, Extreme: 1),
            3 => _stateReader.BuildModeCatalog().FirstOrDefault(m =>
                    m.OperatingMode == 3 && m.ProfileIndex == (binding.ProfileIndex ?? 0)),
            _ => null
        };
    }

    private IpcMessage HandleGetModeCatalog()
    {
        var catalog = _stateReader.BuildModeCatalog()
            .Select(m => new
            {
                m.Label,
                m.Action,
                m.OperatingMode,
                m.ProfileIndex,
                m.Silent,
                m.Extreme
            });
        var binding = _appProfiles.Current;
        return IpcMessage.Create(IpcMessageType.Ack, new
        {
            Success = true,
            Catalog = catalog,
            AppBinding = new { binding.Enabled, binding.RestoreOnLeave, binding.Profiles }
        });
    }

    private IpcMessage HandleSetAppBindingEnabled(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetAppBindingEnabledRequest>();
        if (req == null) return Error("Invalid payload");
        _appProfiles.SetEnabled(req.Enabled);
        _autoAppliedMode = null;
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, req.Enabled });
    }

    private IpcMessage HandleSaveAppProfile(IpcMessage msg)
    {
        var req = msg.DeserializePayload<AppProfileRequest>();
        if (req is null || string.IsNullOrWhiteSpace(req.Process))
            return Error("Invalid payload: process required");

        _appProfiles.SaveProfile(new AppProfile
        {
            Process = req.Process.Trim().ToLowerInvariant(),
            Mode = req.Mode,
            ProfileIndex = req.ProfileIndex,
            Silent = req.Silent,
            Extreme = req.Extreme,
            Label = req.Label ?? ""
        });
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = true });
    }

    private IpcMessage HandleDeleteAppProfile(IpcMessage msg)
    {
        var req = msg.DeserializePayload<AppProfileRequest>();
        if (req is null) return Error("Invalid payload");
        var removed = _appProfiles.DeleteProfile(req.Process);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = removed });
    }

    private IpcMessage HandleEcDiagnostic()
    {
        try
        {
            var report = _acpi.RunDiagnostic();
            _logger.LogInformation("EC Diagnostic:\n{Report}", report);
            return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, Report = report });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EC Diagnostic failed");
            return Error($"EC Diagnostic error: {ex.Message}");
        }
    }

    private IpcMessage HandleKernelEcDiagnostic()
    {
        try
        {
            if (!_kernelAcpi.IsOpen)
                return Error("Kernel ACPI device is not open. Was the driver started?");

            var report = _kernelAcpi.DiscoverIoctl();
            _logger.LogInformation("Kernel EC Diagnostic:\n{Report}", report);
            return IpcMessage.Create(IpcMessageType.Ack, new { Success = true, Report = report });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kernel EC Diagnostic failed");
            return Error($"Kernel EC Diagnostic error: {ex.Message}");
        }
    }
}

// Internal request DTOs
internal record SetModeRequest(int Mode, int? ProfileIndex = null, int? Silent = null, int? Extreme = null);
internal record SetFanBoostRequest(bool Enable);
internal record SetTurboOcRequest(int Offset);
internal record SetAppBindingEnabledRequest(bool Enabled);
internal record AppProfileRequest(string Process, int Mode, int? ProfileIndex, int? Silent, int? Extreme, string? Label);
