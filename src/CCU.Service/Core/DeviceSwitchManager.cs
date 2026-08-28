using CCU.Shared.Models;
using CCU.Service.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Core;

/// <summary>
/// 设备开关管理器
///
/// 管理:
///   - 摄像头 启用/禁用 (PNP Device *Webcam* Camera)
///   - 独显 启用/禁用 (PNP Device *NVIDIA* Display)
///   - AMD Audio CoProcessor 启用/禁用
///   - 蓝牙 启用/禁用
///   - 飞行模式 (虚拟 HID)
/// </summary>
public class DeviceSwitchManager
{
    private readonly ILogger<DeviceSwitchManager> _logger;
    private readonly PnpDeviceController _pnp;

    public DeviceSwitchManager(ILogger<DeviceSwitchManager> logger, PnpDeviceController pnp)
    {
        _logger = logger;
        _pnp = pnp;
    }

    public bool IsWebcamEnabled => _pnp.IsDeviceEnabled("*Webcam*", DeviceClass.Camera) ||
                                    _pnp.IsDeviceEnabled("*Camera*", DeviceClass.Camera);

    public bool IsDGpuEnabled => _pnp.IsDeviceEnabled("*NVIDIA*", DeviceClass.Display);

    public bool SetWebcam(bool enable)
    {
        return enable
            ? _pnp.EnableDevice("*Webcam*", DeviceClass.Camera) || _pnp.EnableDevice("*Camera*", DeviceClass.Camera)
            : _pnp.DisableDevice("*Webcam*", DeviceClass.Camera) && _pnp.DisableDevice("*Camera*", DeviceClass.Camera);
    }

    public bool SetDGpu(bool enable)
    {
        return enable
            ? _pnp.EnableDevice("*NVIDIA*", DeviceClass.Display)
            : _pnp.DisableDevice("*NVIDIA*", DeviceClass.Display);
    }

    public bool SetAmdAudioCoProcessor(bool enable)
    {
        return enable
            ? _pnp.EnableDevice("*AMD Audio CoProcessor*", DeviceClass.System)
            : _pnp.DisableDevice("*AMD Audio CoProcessor*", DeviceClass.System);
    }

    public bool ToggleWebcam() => SetWebcam(!IsWebcamEnabled);
    public bool ToggleDGpu() => SetDGpu(!IsDGpuEnabled);
}
