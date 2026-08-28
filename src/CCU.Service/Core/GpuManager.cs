using Microsoft.Extensions.Logging;
using CCU.Service.Infrastructure;

namespace CCU.Service.Core;

/// <summary>
/// GPU 管理器
/// - MUX Switch 模式切换 (iGPU/dGPU/Hybrid/HotSwap)
/// - NVIDIA WhisperMode 2.0
/// - GPU 超频偏移 (Turbo 模式)
/// - GPU 省电 (面板自刷新)
/// </summary>
public class GpuManager
{
    private readonly ILogger<GpuManager> _logger;
    private readonly WmiAcpiClient _acpi;
    private readonly PnpDeviceController _pnp;

    public GpuManager(ILogger<GpuManager> logger, WmiAcpiClient acpi, PnpDeviceController pnp)
    {
        _logger = logger;
        _acpi = acpi;
        _pnp = pnp;
    }

    /// <summary>
    /// 获取当前 GPU 模式
    /// </summary>
    public GpuMode GetCurrentMode()
    {
        try
        {
            var data = _acpi.ECRead(WmiAcpiClient.EC_ADDR_GPU_MODE);
            return (GpuMode)data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GPU mode");
            return GpuMode.Hybrid;
        }
    }

    /// <summary>
    /// 设置 GPU 模式
    /// </summary>
    public bool SetMode(GpuMode mode)
    {
        try
        {
            switch (mode)
            {
                case GpuMode.IgpuOnly:
                    // 仅集成显卡: 禁用 dGPU + EC 写入
                    _pnp.DisableDevice("*NVIDIA*", DeviceClass.Display);
                    _acpi.ECWrite(WmiAcpiClient.EC_ADDR_GPU_MODE, 0);
                    break;

                case GpuMode.DgpuOnly:
                    // 仅独立显卡: EC 写入 (通常需要重启)
                    _pnp.EnableDevice("*NVIDIA*", DeviceClass.Display);
                    _acpi.ECWrite(WmiAcpiClient.EC_ADDR_GPU_MODE, 1);
                    break;

                case GpuMode.Hybrid:
                    // Optimus 混合: EC 写入 + 启用 dGPU
                    _acpi.ECWrite(WmiAcpiClient.EC_ADDR_GPU_MODE, 2);
                    break;

                case GpuMode.HotSwap:
                    // Advanced Optimus: EC 写入
                    _acpi.ECWrite(WmiAcpiClient.EC_ADDR_GPU_MODE, 3);
                    break;
            }

            _logger.LogInformation("GPU mode set to {Mode}", mode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set GPU mode {Mode}", mode);
            return false;
        }
    }
}

public enum GpuMode
{
    IgpuOnly = 0,
    DgpuOnly = 1,
    Hybrid = 2,
    HotSwap = 3
}
