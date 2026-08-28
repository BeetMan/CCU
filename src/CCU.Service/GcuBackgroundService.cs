using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CCU.Shared.IPC;
using CCU.Service.Core;
using CCU.Service.Infrastructure;
using System.Text.Json;

namespace CCU.Service;

/// <summary>
/// GCUService 替代 — .NET 8 Worker Service
/// 负责：
///   1. 注册为 Windows Service (GCUBridge 替代)
///   2. 硬件监控循环
///   3. Named Pipe IPC 服务端，接收 UI 命令
///   4. 应用绑定 Profiles 的前台窗口检测
/// </summary>
public class GcuBackgroundService : BackgroundService
{
    private readonly ILogger<GcuBackgroundService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly HardwareMonitorService _hwMonitor;
    private readonly PerformanceManager _perfManager;
    private readonly FanControlManager _fanManager;
    private readonly GpuManager _gpuManager;
    private readonly DeviceSwitchManager _deviceSwitchManager;
    private readonly WmiAcpiClient _acpi;
    private readonly KernelAcpiClient _kernelAcpi;
    private readonly PipeServer _pipeServer;

    public GcuBackgroundService(
        ILogger<GcuBackgroundService> logger,
        IHostApplicationLifetime appLifetime,
        HardwareMonitorService hwMonitor,
        PerformanceManager perfManager,
        FanControlManager fanManager,
        GpuManager gpuManager,
        DeviceSwitchManager deviceSwitchManager,
        WmiAcpiClient acpi,
        KernelAcpiClient kernelAcpi)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _hwMonitor = hwMonitor;
        _perfManager = perfManager;
        _fanManager = fanManager;
        _gpuManager = gpuManager;
        _deviceSwitchManager = deviceSwitchManager;
        _acpi = acpi;
        _kernelAcpi = kernelAcpi;

        _pipeServer = new PipeServer("CCU.Service.Pipe", HandleIpcMessage);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CCU Service starting...");

        // 初始化硬件通信
        _hwMonitor.Initialize();
        _perfManager.Initialize();

        // 打开内核驱动设备 — 直接通过 CreateFile/DeviceIoControl 与 UWACPIDriver.sys 通信
        _kernelAcpi.OpenDevice();
        _logger.LogInformation("Kernel ACPI device opened: {IsOpen}", _kernelAcpi.IsOpen);

        // 启动 IPC 管道
        _pipeServer.Start();
        _logger.LogInformation("IPC pipe server started");

        // 主循环 — 硬件监控 + 应用绑定
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _hwMonitor.Update();

                // TODO: 前台窗口检测 → 应用绑定 Profile 切换

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
                IpcMessageType.SetGpuMode => HandleSetGpuMode(message),
                IpcMessageType.SetFanTable => HandleSetFanTable(message),
                IpcMessageType.SetDeviceSwitch => HandleSetDeviceSwitch(message),
                IpcMessageType.EcDiagnostic => HandleEcDiagnostic(),
                IpcMessageType.KernelEcDiagnostic => HandleKernelEcDiagnostic(),
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
        var info = new Shared.Models.HardwareInfo
        {
            CpuTemperature = _hwMonitor.GetCpuTemperature() ?? 0,
            GpuTemperature = _hwMonitor.GetGpuTemperature() ?? 0,
            CpuUsage = _hwMonitor.GetCpuUsage() ?? 0,
            GpuUsage = _hwMonitor.GetGpuUsage() ?? 0,
            Timestamp = DateTime.UtcNow
        };
        return IpcMessage.Create(IpcMessageType.HardwareInfoUpdate, info);
    }

    private IpcMessage HandleSetPerformanceMode(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetModeRequest>();
        if (req == null) return Error("Invalid payload");
        var result = _perfManager.SetMode(req.Mode);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = result });
    }

    private IpcMessage HandleSetGpuMode(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetGpuModeRequest>();
        if (req == null) return Error("Invalid payload");
        var result = _gpuManager.SetMode((GpuMode)req.Mode);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = result });
    }

    private IpcMessage HandleSetFanTable(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetFanTableRequest>();
        if (req?.Table == null) return Error("Invalid payload");
        var result = _fanManager.ApplyFanTable(req.Table);
        return IpcMessage.Create(IpcMessageType.Ack, new { Success = result });
    }

    private IpcMessage HandleSetDeviceSwitch(IpcMessage msg)
    {
        var req = msg.DeserializePayload<SetDeviceSwitchRequest>();
        if (req == null) return Error("Invalid payload");

        bool result = req.Device switch
        {
            "webcam" => _deviceSwitchManager.SetWebcam(req.Enable),
            "dgpu" => _deviceSwitchManager.SetDGpu(req.Enable),
            "amdacp" => _deviceSwitchManager.SetAmdAudioCoProcessor(req.Enable),
            _ => false
        };

        return IpcMessage.Create(IpcMessageType.Ack, new { Success = result });
    }

    private static IpcMessage Error(string msg) =>
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
internal record SetModeRequest(int Mode);
internal record SetGpuModeRequest(int Mode);
internal record SetFanTableRequest(Shared.Models.FanTable Table);
internal record SetDeviceSwitchRequest(string Device, bool Enable);
