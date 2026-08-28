using CCU.Shared.Models;
using Microsoft.Extensions.Logging;
using CCU.Service.Infrastructure;

namespace CCU.Service.Core;

/// <summary>
/// 风扇控制管理器
///
/// 原厂通过 SmartApcTable (SMAPCTABLE) 传递风扇曲线到 EC。
/// SMAPCTABLE 是 256 字节的 BIOS-EC 共享内存结构，
/// 通过 IOCTL_GPD_ACPI_SMAPCTABLE (0x9C40A0C0) 或 DLL 中的 SMAPCTable() 函数读写。
///
/// 替代方案: 通过 WMI GetSetULong2 方法直接读写 SMAPCTABLE。
/// </summary>
public class FanControlManager
{
    private readonly ILogger<FanControlManager> _logger;
    private readonly WmiAcpiClient _acpi;

    // GetSetULong2 命令常量
    private const ulong CMD_READ_SMART_APC_TABLE = 12;
    private const ulong CMD_WRITE_SMART_APC_TABLE = 13;

    // SMAPCTABLE 风扇曲线偏移 (基于原厂分析)
    private const int CPU_FAN_CURVE_OFFSET = 0x00;
    private const int GPU_FAN_CURVE_OFFSET = 0x20;

    // 每个风扇 16 个节点, 每个节点 2 字节 (温度 + 占空比)
    private const int FAN_CURVE_NODES = 16;
    private const int FAN_NODE_SIZE = 2;

    public FanControlManager(ILogger<FanControlManager> logger, WmiAcpiClient acpi)
    {
        _logger = logger;
        _acpi = acpi;
    }

    /// <summary>
    /// 读取当前 CPU 风扇曲线
    /// </summary>
    public List<FanCurvePoint> GetCpuFanCurve()
    {
        return ReadFanCurve(CPU_FAN_CURVE_OFFSET);
    }

    /// <summary>
    /// 读取当前 GPU 风扇曲线
    /// </summary>
    public List<FanCurvePoint> GetGpuFanCurve()
    {
        return ReadFanCurve(GPU_FAN_CURVE_OFFSET);
    }

    /// <summary>
    /// 应用风扇曲线 (写入 EC)
    /// </summary>
    public bool ApplyFanTable(FanTable table)
    {
        try
        {
            if (table.CpuCurve.Count > 0)
                WriteFanCurve(CPU_FAN_CURVE_OFFSET, table.CpuCurve);

            if (table.GpuCurve.Count > 0)
                WriteFanCurve(GPU_FAN_CURVE_OFFSET, table.GpuCurve);

            // 写入 EC 风扇独立控制标志
            _acpi.ECWrite(WmiAcpiClient.EC_ADDR_COOLING_MODE,
                table.FanControlRespective ? (byte)1 : (byte)0);

            _logger.LogInformation("Fan table '{Name}' applied", table.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply fan table");
            return false;
        }
    }

    /// <summary>
    /// 设置风扇 Boost (最大转速)
    /// </summary>
    public bool SetFanBoost(bool enable)
    {
        try
        {
            // FanBoost EC 地址 (需确认具体值)
            _acpi.ECWrite(0x04D0, enable ? (byte)1 : (byte)0);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set fan boost");
            return false;
        }
    }

    private List<FanCurvePoint> ReadFanCurve(int offset)
    {
        var curve = new List<FanCurvePoint>();
        for (int i = 0; i < FAN_CURVE_NODES; i++)
        {
            int addr = (ushort)(offset + i * FAN_NODE_SIZE);
            var raw = _acpi.ECReadWord((ushort)addr);

            var point = new FanCurvePoint
            {
                UpTemperature = (raw >> 8) & 0xFF,    // 上升温度
                DownTemperature = (raw >> 8) & 0xFF,    // 下降温度 (= 上升温度 - 2, 典型)
                Duty = raw & 0xFF                       // 占空比
            };
            curve.Add(point);
        }
        return curve;
    }

    private void WriteFanCurve(int offset, List<FanCurvePoint> curve)
    {
        for (int i = 0; i < Math.Min(curve.Count, FAN_CURVE_NODES); i++)
        {
            int addr = (ushort)(offset + i * FAN_NODE_SIZE);
            var point = curve[i];
            ushort raw = (ushort)((point.UpTemperature << 8) | point.Duty);
            _acpi.ECWrite((ushort)addr, (byte)(raw & 0xFF));
            _acpi.ECWrite((ushort)(addr + 1), (byte)((raw >> 8) & 0xFF));
        }
    }
}
