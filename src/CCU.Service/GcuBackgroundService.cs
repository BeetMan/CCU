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
    private readonly WmiAcpiClient _acpi;
    private readonly KernelAcpiClient _kernelAcpi;
    private readonly PipeServer _pipeServer;

    public GcuBackgroundService(
        ILogger<GcuBackgroundService> logger,
        IHostApplicationLifetime appLifetime,
        HardwareMonitorService hwMonitor,
        VendorMqttControl mqtt,
        VendorStateReader stateReader,
        WmiAcpiClient acpi,
        KernelAcpiClient kernelAcpi)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _hwMonitor = hwMonitor;
        _mqtt = mqtt;
        _stateReader = stateReader;
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

                // TODO (阶段2): 前台窗口检测 → 应用绑定 Profile 自动切换

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
