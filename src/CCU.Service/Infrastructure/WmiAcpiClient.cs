using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// WMI/ACPI 通信客户端 — 替代原厂 ACPIDriverDll
///
/// 通过 root\wmi 命名空间中的 AcpiTest_MULong WMI 类读写 EC 寄存器。
/// 核心方法: GetSetULong(Addr, Value) — 相当于 ACPI _DSM method 调用。
/// </summary>
public class WmiAcpiClient
{
    private readonly ILogger<WmiAcpiClient> _logger;
    private ManagementObject? _acpiInstance;
    private readonly object _lock = new();

    // 原厂定义的常量
    public const byte SMRW_CMD_READ = 187;   // 0xBB
    public const byte SMRW_CMD_WRITE = 170;  // 0xAA

    // GetSetULong2 参数偏移
    public const int ADDR_OFFSET = 0;        // ulong bits 0-31: 地址
    public const int VALUE_OFFSET = 32;      // ulong bits 32-63: 值（写时）
    public const int OFFSET_OFFSET = 40;     // ulong bits 40-55: 偏移
    public const int CMD_OFFSET = 56;        // ulong bits 56-63: 命令

    public WmiAcpiClient(ILogger<WmiAcpiClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 初始化 WMI ACPI 连接
    /// </summary>
    private static readonly string DiagLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "CCU_Alternative", "wmi_init_log.txt");

    public bool Initialize()
    {
        try
        {
            // 写诊断文件 (LocalSystem 无权写用户目录，必须写 ProgramData)
            var diagDir = Path.GetDirectoryName(DiagLogPath)!;
            if (!Directory.Exists(diagDir)) Directory.CreateDirectory(diagDir);
            File.AppendAllText(DiagLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} WmiAcpiClient.Initialize() called, user={Environment.UserName}\n");

            var scope = new ManagementScope(@"\\.\root\wmi");
            var query = new ObjectQuery("SELECT * FROM AcpiTest_MULong");
            using var searcher = new ManagementObjectSearcher(scope, query);

            int count = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                _acpiInstance = obj;
                count++;
                _logger.LogInformation("WMI AcpiTest_MULong instance found: {Path}", obj.Path);
            }

            if (count > 0)
            {
                File.AppendAllText(DiagLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} SUCCESS: found {count} AcpiTest_MULong instances\n");
                _logger.LogInformation("WMI ACPI initialized: {Count} AcpiTest_MULong instance(s)", count);
                return true;
            }

            File.AppendAllText(DiagLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} WARNING: found 0 instances (user={Environment.UserName})\n");
            _logger.LogWarning("WMI AcpiTest_MULong instance not found (running as {User})", Environment.UserName);
            return false;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(DiagLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ERROR: {ex.GetType().Name}: {ex.Message}\n"); } catch { /* best effort */ }
            _logger.LogError(ex, "Failed to initialize WMI ACPI connection: {Type} — {Message}",
                ex.GetType().Name, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 通用 ACPI GetSetUlong 调用
    /// </summary>
    private ulong GetSetUlong(ulong data)
    {
        if (_acpiInstance == null)
            throw new InvalidOperationException("ACPI WMI not initialized");

        var inParams = _acpiInstance.GetMethodParameters("GetSetULong");
        inParams["Data"] = data;

        lock (_lock)
        {
            var outParams = _acpiInstance.InvokeMethod("GetSetULong", inParams, null);
            if (outParams == null)
                throw new InvalidOperationException("GetSetULong returned null");

            // 返回值可能是 UInt32 或 UInt64，取决于 ACPI 驱动版本
            object raw = outParams["Return"];
            return raw switch
            {
                ulong u64 => u64,
                uint u32 => u32,
                int i32 when i32 >= 0 => (ulong)i32,
                _ => Convert.ToUInt64(raw)
            };
        }
    }

    /// <summary>
    /// 读 EC RAM 地址
    /// </summary>
    public byte ECRead(ushort addr)
    {
        var cmd = BuildSMRWCommand(SMRW_CMD_READ, addr, 0);
        var result = GetSetUlong(cmd);
        // GetSetULong2 格式：返回值低 8 位是 EC 数据
        return (byte)(result & 0xFF);
    }

    /// <summary>
    /// 写 EC RAM 地址
    /// </summary>
    public void ECWrite(ushort addr, byte value)
    {
        var cmd = BuildSMRWCommand(SMRW_CMD_WRITE, addr, value);
        GetSetUlong(cmd);
    }

    /// <summary>
    /// 读 EC 一个字 (16-bit)
    /// </summary>
    public ushort ECReadWord(ushort addr)
    {
        byte lo = ECRead(addr);
        byte hi = ECRead((ushort)(addr + 1));
        return (ushort)(lo | (hi << 8));
    }

    /// <summary>
    /// 读 EC 一个双字 (32-bit)
    /// </summary>
    public uint ECReadDword(ushort addr)
    {
        ushort lo = ECReadWord(addr);
        ushort hi = ECReadWord((ushort)(addr + 2));
        return (uint)(lo | (hi << 16));
    }

    /// <summary>
    /// 构建 SMRW (Smart Read/Write) 命令 — GetSetULong2 64位格式
    ///
    /// GetSetULong2 数据格式 (64-bit):
    ///   bits 0-31:  地址 (Address)
    ///   bits 32-39: 值 (Value)
    ///   bits 40-55: 偏移 (Offset)
    ///   bits 56-63: 命令 (Command)
    ///
    /// SMRW Read:  cmd=0xBB(187), offset=0, value=0
    /// SMRW Write: cmd=0xAA(170), offset=0
    /// </summary>
    private static ulong BuildSMRWCommand(byte cmd, ushort addr, byte value)
    {
        return ((ulong)cmd << 56) | ((ulong)value << 32) | (ulong)addr;
    }

    // === EC 地址常量 (从原厂软件中提取) ===

    /// <summary>
    /// 冷却模式 EC 地址 (原厂: CoolingModeECAddress = 1991 / 0x07C7)
    /// </summary>
    public const ushort EC_ADDR_COOLING_MODE = 0x07C7;

    /// <summary>
    /// 性能模式 EC 地址
    /// </summary>
    public const ushort EC_ADDR_PERFORMANCE_MODE = 0x04CC;

    /// <summary>
    /// CPU 温度 EC 地址
    /// </summary>
    public const ushort EC_ADDR_CPU_TEMP = 0x07CD;

    /// <summary>
    /// GPU 温度 EC 地址
    /// </summary>
    public const ushort EC_ADDR_GPU_TEMP = 0x07CE;

    /// <summary>
    /// CPU 风扇转速 EC 地址
    /// </summary>
    public const ushort EC_ADDR_CPU_FAN = 0x07C8;

    /// <summary>
    /// GPU 风扇转速 EC 地址
    /// </summary>
    public const ushort EC_ADDR_GPU_FAN = 0x07C9;

    /// <summary>
    /// GPU MUX 模式 EC 地址
    /// </summary>
    public const ushort EC_ADDR_GPU_MODE = 0x04E2;

    /// <summary>
    /// EC 诊断 — 逐字节分析 GetSetULong 返回值
    /// 返回详细日志，用于验证数据编码格式
    /// </summary>
    public string RunDiagnostic()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== EC Diagnostic @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine($"User: {Environment.UserName}");
        sb.AppendLine($"AcpiInstance: {(_acpiInstance != null)}");

        if (_acpiInstance == null)
        {
            sb.AppendLine("ERROR: ACPI not initialized");
            return sb.ToString();
        }

        // 列出所有 WMI 方法和属性
        try
        {
            sb.AppendLine();
            sb.AppendLine("--- Available Methods (via CIM) ---");
            var mc = new ManagementClass(_acpiInstance.Path.Path);
            foreach (MethodData m in mc.Methods)
                sb.AppendLine($"  Method: {m.Name}");
            foreach (var key in _acpiInstance.Properties)
                sb.AppendLine($"  Property: {key.Name} = {key.Value}");
        }
        catch (Exception ex) { sb.AppendLine($"  Error listing methods: {ex.Message}"); }

        // 测试多个地址的 SMRW Read
        ushort[] testAddrs = { 0x04CC, 0x07C7, 0x07CD, 0x07C8, 0x04E2 };
        foreach (var addr in testAddrs)
        {
            try
            {
                var cmd = BuildSMRWCommand(SMRW_CMD_READ, addr, 0);
                var raw = GetSetUlong(cmd);
                sb.AppendLine();
                sb.AppendLine($"--- Read 0x{addr:X4} ---");
                sb.AppendLine($"  Input:  0x{cmd:X16}");
                sb.AppendLine($"  Return: 0x{raw:X16}");
                sb.AppendLine($"  Byte[0:7]  = 0x{(byte)(raw & 0xFF):X2}");
                sb.AppendLine($"  Byte[8:15] = 0x{(byte)((raw >> 8) & 0xFF):X2}");
                sb.AppendLine($"  Byte[16:23]= 0x{(byte)((raw >> 16) & 0xFF):X2}");
                sb.AppendLine($"  Byte[24:31]= 0x{(byte)((raw >> 24) & 0xFF):X2}");
                sb.AppendLine($"  Byte[32:39]= 0x{(byte)((raw >> 32) & 0xFF):X2}");
                sb.AppendLine($"  Byte[40:47]= 0x{(byte)((raw >> 40) & 0xFF):X2}");
                sb.AppendLine($"  Byte[48:55]= 0x{(byte)((raw >> 48) & 0xFF):X2}");
                sb.AppendLine($"  Byte[56:63]= 0x{(byte)((raw >> 56) & 0xFF):X2}");
            }
            catch (Exception ex) { sb.AppendLine($"  ERROR: {ex.GetType().Name}: {ex.Message}"); }
        }

        // 扫描所有 WMI 方法，找真正的 EC 操作方法
        sb.AppendLine();
        sb.AppendLine("--- All WMI Methods (both approaches) ---");
        try
        {
            // 方法一：ManagementClass
            try
            {
                using var mc = new ManagementClass(@"\\.\root\wmi:AcpiTest_MULong");
                sb.AppendLine($"  [ManagementClass] mc.Methods.Count = {mc.Methods.Count}");
                foreach (MethodData m in mc.Methods)
                {
                    sb.AppendLine($"  Method: {m.Name}");
                    try
                    {
                        if (m.InParameters != null)
                            foreach (PropertyData p in m.InParameters.Properties)
                                sb.AppendLine($"    In: {p.Name} ({p.Type})");
                    }
                    catch { }
                    try
                    {
                        if (m.OutParameters != null)
                            foreach (PropertyData p in m.OutParameters.Properties)
                                sb.AppendLine($"    Out: {p.Name} ({p.Type})");
                    }
                    catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine($"  ManagementClass failed: {ex.Message}"); }

            // 方法二：meta_class
            try
            {
                using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi",
                    "SELECT * FROM meta_class WHERE __CLASS = 'AcpiTest_MULong'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    sb.AppendLine("  [meta_class search] Methods:");
                    // Access methods through the ManagementClass
                    using var mc2 = new ManagementClass(obj.Path.ToString());
                    foreach (MethodData m in mc2.Methods)
                        sb.AppendLine($"  Method: {m.Name}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"  meta_class failed: {ex.Message}"); }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        // Test: 调用 GetULong + SetULong（UInt32），看看是不是分开的函数
        sb.AppendLine();
        sb.AppendLine("--- GetULong / SetULong / FireULong tests ---");

        // GetULong
        try
        {
            var inGet = _acpiInstance.GetMethodParameters("GetULong");
            var outGet = _acpiInstance.InvokeMethod("GetULong", inGet, null);
            var data = (uint)outGet["Data"];
            sb.AppendLine($"  GetULong() → Data=0x{data:X8} ({data})");
        }
        catch (Exception ex) { sb.AppendLine($"  GetULong ERROR: {ex.Message}"); }

        // FireULong
        try
        {
            var inFire = _acpiInstance.GetMethodParameters("FireULong");
            inFire["Hack"] = 0x04CC;
            var outFire = _acpiInstance.InvokeMethod("FireULong", inFire, null);
            sb.AppendLine($"  FireULong(Hack=0x04CC) → Return=0x{(uint)outFire["Return"]:X8}");
        }
        catch (Exception ex) { sb.AppendLine($"  FireULong ERROR: {ex.Message}"); }

        // GetButton
        try
        {
            var inBtn = _acpiInstance.GetMethodParameters("GetButton");
            inBtn["Data"] = (ulong)0;
            var outBtn = _acpiInstance.InvokeMethod("GetButton", inBtn, null);
            var btnData = (uint)outBtn["Return"];
            sb.AppendLine($"  GetButton(0) → Return=0x{btnData:X8}");
        }
        catch (Exception ex) { sb.AppendLine($"  GetButton ERROR: {ex.Message}"); }

        // GetButton with address as Data
        try
        {
            var inBtn = _acpiInstance.GetMethodParameters("GetButton");
            inBtn["Data"] = (ulong)0x04CC;
            var outBtn = _acpiInstance.InvokeMethod("GetButton", inBtn, null);
            var btnData = (uint)outBtn["Return"];
            sb.AppendLine($"  GetButton(0x04CC) → Return=0x{btnData:X8}, byte0=0x{(byte)btnData:X2}");
        }
        catch (Exception ex) { sb.AppendLine($"  GetButton ERROR: {ex.Message}"); }

        // 测试 EC 读取: GetButton with SMRW read cmd
        try
        {
            var smrw = ((ulong)0xBB << 56) | (ulong)0x07C7;
            var inBtn = _acpiInstance.GetMethodParameters("GetButton");
            inBtn["Data"] = smrw;
            var outBtn = _acpiInstance.InvokeMethod("GetButton", inBtn, null);
            var ret = outBtn["Return"];
            sb.AppendLine($"  GetButton(SMRW_Read_0x07C7) → Return type={ret.GetType().Name}, value=0x{Convert.ToUInt64(ret):X16}");
        }
        catch (Exception ex) { sb.AppendLine($"  GetButton ERROR: {ex.Message}"); }

        // SetULong write test
        try
        {
            var inSet = _acpiInstance.GetMethodParameters("SetULong");
            inSet["Data"] = (uint)0x04CC;  // addr as value
            var outSet = _acpiInstance.InvokeMethod("SetULong", inSet, null);
            sb.AppendLine($"  SetULong(0x04CC) → OK");
        }
        catch (Exception ex) { sb.AppendLine($"  SetULong ERROR: {ex.Message}"); }

        // 测试其他可能的 EC 类
        sb.AppendLine();
        sb.AppendLine("--- Testing alternative WMI classes ---");

        // 重点测试 AcpiTest_QULong 的方法（可能是真正的 Query ULong）
        sb.AppendLine();
        sb.AppendLine("--- Testing AcpiTest_QULong (likely real EC interface) ---");
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM AcpiTest_QULong");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["InstanceName"]?.ToString() ?? "?";
                sb.AppendLine($"  Instance: {name}");

                // 列出方法
                var mc = new ManagementClass(@"\\.\root\wmi:AcpiTest_QULong");
                foreach (MethodData m in mc.Methods)
                    sb.AppendLine($"  Method: {m.Name}");

                // 尝试调用 GetSetULong
                try
                {
                    var inParams = obj.GetMethodParameters("GetSetULong");
                    foreach (PropertyData p in inParams.Properties)
                        sb.AppendLine($"     InParam: {p.Name} ({p.Type})");
                    inParams["Data"] = (ulong)0xBB000000000004CC;
                    var outParams = obj.InvokeMethod("GetSetULong", inParams, null);
                    if (outParams != null)
                    {
                        var ret = outParams["Return"];
                        sb.AppendLine($"     GetSetULong(SMRW_Read_0x04CC) → Return=0x{Convert.ToUInt64(ret):X16}");
                    }
                }
                catch (Exception ex) { sb.AppendLine($"     GetSetULong ERROR: {ex.Message}"); }

                // 尝试 GetButton
                try
                {
                    var inParams = obj.GetMethodParameters("GetButton");
                    foreach (PropertyData p in inParams.Properties)
                        sb.AppendLine($"     GetButton Param: {p.Name} ({p.Type})");
                    inParams["Data"] = (ulong)0;
                    var outParams = obj.InvokeMethod("GetButton", inParams, null);
                    var ret = outParams["Return"];
                    sb.AppendLine($"     GetButton(0) → Return=0x{Convert.ToUInt64(ret):X16}");
                }
                catch (Exception ex) { sb.AppendLine($"     GetButton ERROR: {ex.Message}"); }

                // 尝试各种方法名
                foreach (var methodName in new[] { "QueryULong", "Query", "QueryULongData", "GetULong", "SetULong", "ReadEC", "WriteEC", "FireULong", "GetButton" })
                {
                    try
                    {
                        var inP = obj.GetMethodParameters(methodName);
                        foreach (PropertyData p in inP.Properties)
                            sb.AppendLine($"     {methodName} Param: {p.Name} ({p.Type})");
                    }
                    catch (System.Management.ManagementException mex) when (mex.Message.Contains("NotFound"))
                    { } // skip
                    catch (Exception ex) { sb.AppendLine($"     {methodName} GetMethodParameters: {ex.Message}"); }
                }

                break; // 只测第一个实例
            }
        }
        catch (Exception ex) { sb.AppendLine($"  AcpiTest_QULong section: {ex.Message}"); }

        // 测试 AcpiTest_QSString (可能是字符串参数版本)
        sb.AppendLine();
        sb.AppendLine("--- Testing AcpiTest_QSString ---");
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM AcpiTest_QSString");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["InstanceName"]?.ToString() ?? "?";
                sb.AppendLine($"  Instance: {name}");
                var mc = new ManagementClass(@"\\.\root\wmi:AcpiTest_QSString");
                foreach (MethodData m in mc.Methods)
                    sb.AppendLine($"  Method: {m.Name}");
                break;
            }
        }
        catch (Exception ex) { sb.AppendLine($"  AcpiTest_QSString: {ex.Message}"); }

        // MSAcpi_ThermalZoneTemperature
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                sb.AppendLine($"  MSAcpi_ThermalZone: {obj["InstanceName"]}");
                sb.AppendLine($"    CurrentTemp = {obj["CurrentTemperature"]}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  MSAcpi_ThermalZone: {ex.Message}"); }

        // 搜索含 EC/Embed/Ctrl 的类
        sb.AppendLine();
        sb.AppendLine("--- Searching for EC/Embed/Ctrl related ---");
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM meta_class");
            foreach (ManagementObject obj in searcher.Get())
            {
                var cn = obj["__CLASS"]?.ToString() ?? "";
                if (cn.IndexOf("EC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cn.IndexOf("Embed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cn.IndexOf("Smbus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cn.IndexOf("IO_", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine($"  {cn}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Search error: {ex.Message}"); }
        sb.AppendLine();
        sb.AppendLine("--- Scan ALL AcpiTest_MULong instances ---");
        sb.AppendLine($"  Only tested InstanceName={_acpiInstance["InstanceName"]}");
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM AcpiTest_MULong");
            int idx = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                idx++;
                var name = obj["InstanceName"]?.ToString() ?? "?";
                sb.AppendLine($"  Instance #{idx}: {name}");
                try
                {
                    var inParams = obj.GetMethodParameters("GetSetULong");
                    inParams["Data"] = (ulong)0;
                    var outParams = obj.InvokeMethod("GetSetULong", inParams, null);
                    var ret = outParams["Return"];
                    sb.AppendLine($"    GetSetULong(0) → Return=0x{Convert.ToUInt64(ret):X16}");
                }
                catch (Exception ex) { sb.AppendLine($"    GetSetULong(0) ERROR: {ex.Message}"); }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Scan error: {ex.Message}"); }

        // 扫描 root\wmi 所有 ACPI 相关类
        sb.AppendLine();
        sb.AppendLine("--- All ACPI WMI classes in root\\wmi ---");
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM meta_class");
            foreach (ManagementObject obj in searcher.Get())
            {
                var className = obj["__CLASS"]?.ToString() ?? "";
                if (className.Contains("Acpi", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("CSD", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("TSD", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Cst", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Pss", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Xpss", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Tss", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Pct", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Control", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Trace", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("MSAcpi", StringComparison.OrdinalIgnoreCase) &&
                    !className.Contains("Info", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"  Class: {className}");
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Search error: {ex.Message}"); }

        sb.AppendLine();

        // 写到 ProgramData 方便即时查看
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                               "CCU_Alternative", "ec_diag_result.txt");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());

        return sb.ToString();
    }
}
