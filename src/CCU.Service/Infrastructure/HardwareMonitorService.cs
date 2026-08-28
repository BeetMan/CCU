using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 硬件监控服务 — 基于 LibreHardwareMonitorLib
/// 直接复用原厂使用的成熟开源库
/// </summary>
public class HardwareMonitorService : IDisposable
{
    private readonly ILogger<HardwareMonitorService> _logger;
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor = new();

    public HardwareMonitorService(ILogger<HardwareMonitorService> logger)
    {
        _logger = logger;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsBatteryEnabled = true,
            IsNetworkEnabled = false,  // 可用 System.Net.NetworkInformation
            IsPsuEnabled = false,
            IsStorageEnabled = true
        };
    }

    public void Initialize()
    {
        _computer.Open();
        _logger.LogInformation("HardwareMonitor initialized");
    }

    /// <summary>
    /// 刷新所有传感器数据
    /// </summary>
    public void Update()
    {
        _computer.Accept(_updateVisitor);
    }

    /// <summary>
    /// 获取 CPU 温度
    /// </summary>
    public float? GetCpuTemperature()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;
            hw.Update();

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature &&
                    sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                {
                    return sensor.Value ?? 0f;
                }
            }

            // 备选：取第一个可用的温度传感器
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature)
                    return sensor.Value ?? 0f;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取 GPU 温度
    /// </summary>
    public float? GetGpuTemperature()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.GpuNvidia &&
                hw.HardwareType != HardwareType.GpuAmd) continue;
            hw.Update();

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature &&
                    sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                {
                    return sensor.Value ?? 0f;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 获取 CPU 使用率
    /// </summary>
    public float? GetCpuUsage()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;
            hw.Update();

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Load &&
                    sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                {
                    return sensor.Value ?? 0f;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 获取 GPU 使用率
    /// </summary>
    public float? GetGpuUsage()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.GpuNvidia &&
                hw.HardwareType != HardwareType.GpuAmd) continue;
            hw.Update();

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Load &&
                    sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                {
                    return sensor.Value ?? 0f;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 获取风扇转速列表
    /// </summary>
    public List<FanInfo> GetFanSpeeds()
    {
        var fans = new List<FanInfo>();

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Fan)
                {
                    fans.Add(new FanInfo
                    {
                        Name = sensor.Name,
                        HardwareName = hw.Name,
                        Rpm = sensor.Value ?? 0f
                    });
                }
            }
        }

        return fans;
    }

    public void Dispose()
    {
        _computer.Close();
    }

    public class FanInfo
    {
        public string Name { get; set; } = "";
        public string HardwareName { get; set; } = "";
        public float Rpm { get; set; }
    }

    private class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var sub in hardware.SubHardware) sub.Accept(this); }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
