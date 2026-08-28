// EcProbe v4 — 基于正确的 SMRW 编码读取 EC 寄存器
// 使用 CWMI 常量 GETSETULONG2 格式 (64-bit value)
// SMRW 编码: CMD[8bit] | VALUE[8bit] | ADDR[16bit]
// 此版本包含完整的 EC 寄存器探测循环

using System.Management;
using CCU.Alternative.EC;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║   EcProbe v4 — EC 寄存器读验证     ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

const byte SMRW_CMD_READ = 187;   // 0xBB — 读命令 (实际使用)
// 写命令 0xAA 见 FanControlManager.CMD_WRITE_SMART_APC_TABLE (写入未开放)

try
{
    var scope = new ManagementScope(@"root\wmi");
    scope.Connect();

    using var searcher = new ManagementObjectSearcher(scope,
        new ObjectQuery("SELECT * FROM AcpiTest_MULong"));
    ManagementObject? mo = null;
    foreach (ManagementObject obj in searcher.Get())
    {
        mo = obj;
        break;
    }

    if (mo == null)
    {
        Console.WriteLine("❌ AcpiTest_MULong 实例未找到");
        return;
    }

    Console.WriteLine("✅ WMI 实例已获取");
    var mp = mo.GetMethodParameters("GetSetULong");
    if (mp == null) { Console.WriteLine("❌ GetSetULong 不可用"); return; }

    // ─── 关键地址列表 ───
    var addrs = new (string label, ushort addr)[]
    {
        ("CoolingMode",     EcRegisterMap.CoolingModeECAddress),  // 1991 (0x07C7)
        ("EC DefaultMode",  EcRegisterMap.EC_DEFAULT_MODE),       // 2024 (0x07E8)
        ("Main Fan L Duty", EcRegisterMap.MAIN_FAN_L_DUTY),       // 1883
        ("Main Fan R Duty", EcRegisterMap.MAIN_FAN_R_DUTY),       // 1884
        ("Main Fan RPM L",  EcRegisterMap.MAIN_FAN_RPM_BYTE1),    // 1124
        ("Main Fan RPM H",  EcRegisterMap.MAIN_FAN_RPM_BYTE2),    // 1125
        ("Second Fan RPM L",EcRegisterMap.SECOND_FAN_RPM_BYTE1),  // 1132
        ("Second Fan RPM H",EcRegisterMap.SECOND_FAN_RPM_BYTE2),  // 1131
        ("GPU Status",      EcRegisterMap.GPU_STATUS),            // 1834
        ("PL1 Setting",     EcRegisterMap.PL1_SETTING_VALUE),     // 1923
        ("PL2 Setting",     EcRegisterMap.PL2_SETTING_VALUE),     // 1924
        ("RGBKB Level R",   EcRegisterMap.RGBKB_LEVEL_R),         // 1897
        ("Lightbar Ctrl",   EcRegisterMap.LIGHTBAR_CONTROL),      // 1864
        ("Fan Alert",       EcRegisterMap.FAN_ALERT_BYTE),        // 1857
        ("Module ID",       EcRegisterMap.ModuleID),              // 2003
        ("Project ID",      EcRegisterMap.PROJECT_ID_BYTE),       // 1856
        ("BatteryAlert",    EcRegisterMap.BATTERY_ALERT),         // 1172
        ("Power Source",    EcRegisterMap.PowSource),             // 1168
    };

    int ok = 0, fail = 0;
    Console.WriteLine();
    Console.WriteLine("─── 读取关键 EC 寄存器 ───");
    foreach (var (label, addr) in addrs)
    {
        // SMRW Read: CMD(8b) | VALUE(0,8b) | ADDR(16b)
        ulong cmd = SMRW_CMD_READ | ((ulong)addr << 16);
        mp["Data"] = cmd;
        try
        {
            var result = mo.InvokeMethod("GetSetULong", mp, null!);
            if (result != null)
            {
                ulong raw = (ulong)result["Return"];
                byte data = (byte)((raw >> 8) & 0xFF);
                Console.WriteLine($"  ✅ {label,-20} [0x{addr:X4}] = 0x{data:X2} ({data})");
                ok++;
            }
            else
            {
                Console.WriteLine($"  ❌ {label,-20} [0x{addr:X4}] null response");
                fail++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ {label,-20} [0x{addr:X4}] {ex.GetType().Name}: {ex.Message}");
            fail++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"─── {ok} 通过, {fail} 失败 ───");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
}
