using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// UEFI 变量读写服务 — 替代原厂 UEFI_Firmware.dll
///
/// 通过 Windows API GetFirmwareEnvironmentVariable/SetFirmwareEnvironmentVariable
/// 或直接通过 NtSetSystemEnvironmentValueEx 操作 UEFI NVRAM 变量。
///
/// 注意: 需要 SeSystemEnvironmentPrivilege 权限 (SYSTEM 用户)。
/// </summary>
public class UefiVariableService
{
    private readonly ILogger<UefiVariableService> _logger;

    public UefiVariableService(ILogger<UefiVariableService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 读 UEFI 变量
    /// </summary>
    public byte[]? ReadVariable(string name, Guid vendorGuid)
    {
        uint size = 0;
        var result = GetFirmwareEnvironmentVariableEx(name, vendorGuid.ToString("B"), null, 0, ref size);

        if (size == 0) return null;

        byte[] buffer = new byte[size];
        result = GetFirmwareEnvironmentVariableEx(name, vendorGuid.ToString("B"), buffer, size, ref size);

        if (result == 0)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to read UEFI var '{Name}': error {Error}", name, err);
            return null;
        }

        return buffer;
    }

    /// <summary>
    /// 写 UEFI 变量
    /// </summary>
    public bool WriteVariable(string name, Guid vendorGuid, byte[] data)
    {
        var result = SetFirmwareEnvironmentVariableEx(name, vendorGuid.ToString("B"), data, (uint)data.Length, 0x00000007); // attributes

        if (result == 0)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to write UEFI var '{Name}': error {Error}", name, err);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 检测高速 SSD 支持 (原厂: IsHighSpeedSSD)
    /// </summary>
    public bool IsHighSpeedSSD()
    {
        // 原厂通过 UEFI_Firmware.dll 的 IsHighSpeedSSD() 导出函数检测
        // 待逆向具体 GUID/variable name
        return false;
    }

    // 常用 UEFI 变量 GUID
    public static readonly Guid EfiGlobalVariable = new("{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}");
    public static readonly Guid EfiOemGuid = new("{EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9}");

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFirmwareEnvironmentVariableEx(
        string lpName,
        string lpGuid,
        byte[]? pBuffer,
        uint nSize,
        ref uint pdwAttribubutes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetFirmwareEnvironmentVariableEx(
        string lpName,
        string lpGuid,
        byte[] pValue,
        uint nSize,
        uint dwAttributes);
}
