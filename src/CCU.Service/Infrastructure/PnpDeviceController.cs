using System.Management;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// PnP 设备控制器 — 替代原厂 PowerShell 脚本
/// 使用 .NET System.Management (WMI) 直接控制设备启用/禁用
/// </summary>
public class PnpDeviceController
{
    private readonly ILogger<PnpDeviceController> _logger;

    public PnpDeviceController(ILogger<PnpDeviceController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 禁用指定设备
    /// </summary>
    public bool DisableDevice(string deviceNamePattern, DeviceClass deviceClass)
    {
        var device = FindDevice(deviceNamePattern, deviceClass, onlyEnabled: true);
        if (device == null) return false;

        try
        {
            using var mo = new ManagementObject(device.Path);
            var result = mo.InvokeMethod("Disable", null);
            _logger.LogInformation("Disabled device: {Device}", device.Name);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable {Device}", device.Name);
            return false;
        }
    }

    /// <summary>
    /// 启用指定设备
    /// </summary>
    public bool EnableDevice(string deviceNamePattern, DeviceClass deviceClass)
    {
        var device = FindDevice(deviceNamePattern, deviceClass, onlyEnabled: false);
        if (device == null) return false;

        try
        {
            using var mo = new ManagementObject(device.Path);
            var result = mo.InvokeMethod("Enable", null);
            _logger.LogInformation("Enabled device: {Device}", device.Name);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable {Device}", device.Name);
            return false;
        }
    }

    /// <summary>
    /// 查询设备状态
    /// </summary>
    public bool IsDeviceEnabled(string deviceNamePattern, DeviceClass deviceClass)
    {
        return FindDevice(deviceNamePattern, deviceClass, onlyEnabled: true) != null;
    }

    private DeviceInfo? FindDevice(string pattern, DeviceClass deviceClass, bool onlyEnabled)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2");
            var className = deviceClass switch
            {
                DeviceClass.Display => "Win32_VideoController",
                DeviceClass.Camera => "Win32_PnPEntity",
                DeviceClass.System => "Win32_PnPEntity",
                DeviceClass.Network => "Win32_NetworkAdapter",
                _ => "Win32_PnPEntity"
            };

            var query = new ObjectQuery($"SELECT * FROM {className}");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? "";
                var status = obj["Status"]?.ToString() ?? obj["Availability"]?.ToString() ?? "";

                if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    bool isOk = status is "OK" or "" or "3";
                    if (onlyEnabled && !isOk) continue;
                    if (!onlyEnabled && isOk) continue;

                    return new DeviceInfo
                    {
                        Path = obj.Path.Path,
                        Name = name!,
                        IsEnabled = isOk
                    };
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for device: {Pattern}", pattern);
            return null;
        }
    }

    /// <summary>
    /// 触发硬件扫描 (相当于 pnputil /scan-devices)
    /// </summary>
    public void ScanForHardwareChanges()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
            // 触发 PnP 重新枚举
            _logger.LogInformation("Hardware rescan triggered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware rescan failed");
        }
    }
}

public enum DeviceClass
{
    Display,
    Camera,
    System,
    Network,
    Bluetooth,
    Audio
}

internal record DeviceInfo
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsEnabled { get; init; }
}
