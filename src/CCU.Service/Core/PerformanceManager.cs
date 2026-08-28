using Microsoft.Extensions.Logging;
using CCU.Service.Infrastructure;

namespace CCU.Service.Core;

/// <summary>
/// 性能模式管理器
/// 通过 EC 寄存器 + 电源计划切换实现 Office/Gaming/Turbo/Custom 四种模式
///
/// 原厂实现:
///   - EC Addr 0x04CC: 写入模式值 (0=Office, 1=Gaming, 2=Turbo, 3=Custom)
///   - 同时调用 PowerSetActiveScheme 切换 Windows 电源计划
///   - Turbo 模式可写 GPU 超频偏移
/// </summary>
public class PerformanceManager
{
    private readonly ILogger<PerformanceManager> _logger;
    private readonly WmiAcpiClient _acpi;
    private readonly PnpDeviceController _pnp;

    // Windows 电源计划 GUID (常见)
    public static readonly Guid PowerSchemeBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSchemeHighPerf = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSchemePowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private int _currentMode;
    public int CurrentMode => _currentMode;

    public PerformanceManager(ILogger<PerformanceManager> logger, WmiAcpiClient acpi, PnpDeviceController pnp)
    {
        _logger = logger;
        _acpi = acpi;
        _pnp = pnp;
    }

    public void Initialize()
    {
        _acpi.Initialize();
        GetCurrentMode();
    }

    /// <summary>
    /// 读取当前性能模式
    /// </summary>
    public int GetCurrentMode()
    {
        try
        {
            var data = _acpi.ECRead(WmiAcpiClient.EC_ADDR_PERFORMANCE_MODE);
            _currentMode = data;
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read performance mode, returning cached value");
            return _currentMode;
        }
    }

    /// <summary>
    /// 设置性能模式
    /// </summary>
    /// <param name="mode">0=Office, 1=Gaming, 2=Turbo, 3=Custom</param>
    public bool SetMode(int mode)
    {
        try
        {
            _acpi.ECWrite(WmiAcpiClient.EC_ADDR_PERFORMANCE_MODE, (byte)mode);
            _currentMode = mode;

            // 同步切换 Windows 电源计划
            SetPowerScheme(mode switch
            {
                0 => PowerSchemePowerSaver,
                1 => PowerSchemeBalanced,
                2 => PowerSchemeHighPerf,
                _ => PowerSchemeBalanced
            });

            _logger.LogInformation("Performance mode set to {Mode}", (PerformanceMode)mode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set performance mode {Mode}: {ErrorType} - {ErrorMessage}",
                mode, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private void SetPowerScheme(Guid schemeGuid)
    {
        try
        {
            PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set power scheme");
        }
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid ActivePolicyGuid);

    private enum PerformanceMode
    {
        Office = 0,
        Gaming = 1,
        Turbo = 2,
        Custom = 3
    }
}
