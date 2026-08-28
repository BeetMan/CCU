// QuickDiag — 超简版硬件诊断，一行命令完成所有验证
// 不依赖 Service, 不依赖 IPC, 不依赖 WPF
// 用法: dotnet run

using System.Management;
using LibreHardwareMonitor.Hardware;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("╔════════════════════════════════╗");
Console.WriteLine("║  CCU QuickDiag — 一键诊断    ║");
Console.WriteLine("╚════════════════════════════════╝");

int ok = 0, ng = 0;
void Check(string label, bool pass, string detail = "")
{
    if (pass) { ok++; Console.WriteLine($"✅ {label}: {detail}"); }
    else { ng++; Console.WriteLine($"❌ {label}: {detail}"); }
}

// 1. WMI ACPI EC
Console.WriteLine("\n── WMI ACPI ──");
try
{
    using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM meta_class WHERE __CLASS = 'AcpiTest_MULong'");
    var found = s.Get().Cast<ManagementObject>().Any();
    Check("AcpiTest_MULong CIM 类", found, found ? "EC 读写可用" : "驱动未加载");

    using var svc = new ManagementObjectSearcher("SELECT State, StartMode FROM Win32_Service WHERE Name = 'GCUBridge'");
    foreach (ManagementObject o in svc.Get())
        Check($"GCUBridge 服务", true, $"State={o["State"]}, StartMode={o["StartMode"]}");
}
catch (Exception ex) { Check("WMI 查询", false, ex.Message); }

// 2. 硬件传感器
Console.WriteLine("\n── 传感器 ──");
try
{
    var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsBatteryEnabled = true };
    c.Open();
    foreach (var hw in c.Hardware)
    {
        hw.Update();
        string label = hw.HardwareType switch { HardwareType.Cpu => "CPU", HardwareType.GpuNvidia => "dGPU", HardwareType.GpuIntel => "iGPU", HardwareType.Memory => "RAM", HardwareType.Battery => "BAT", _ => "" };
        if (string.IsNullOrEmpty(label)) continue;

        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s is { Value: > 0 }).Take(2);
        var loads = hw.Sensors.Where(s => s.SensorType == SensorType.Load && s is { Value: > 0 }).Take(2);
        var fans  = hw.Sensors.Where(s => s.SensorType == SensorType.Fan && s is { Value: > 0 }).Take(2);
        var info  = new List<string>();
        foreach (var s in temps) info.Add($"🌡{s.Name}:{s.Value:F1}°C");
        foreach (var s in loads) info.Add($"📊{s.Name}:{s.Value:F1}%");
        foreach (var s in fans)  info.Add($"🌀{s.Name}:{s.Value:F0}RPM");
        Check(label, true, $"{hw.Name} — {string.Join(" | ", info)}");
    }
    c.Close();
}
catch (Exception ex) { Check("传感器", false, ex.Message); }

// 3. 系统信息
Console.WriteLine("\n── 系统 ──");
try
{
    using var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
    foreach (ManagementObject o in cs.Get())
    {
        var ram = Convert.ToInt64(o["TotalPhysicalMemory"] ?? 0) / 1024 / 1024 / 1024;
        Check("机型", true, $"{o["Manufacturer"]} {o["Model"]} — {ram} GB RAM");
    }
    using var bat = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
    foreach (ManagementObject o in bat.Get())
        Check("电池", true, $"{o["EstimatedChargeRemaining"]}% (插电:{(o["BatteryStatus"]?.ToString()=="2"?"是":"否")})");
}
catch (Exception ex) { Check("系统", false, ex.Message); }

Console.WriteLine($"\n═══════════════════════════════════");
Console.WriteLine($"  {ok} 通过  {ng} 失败");
Console.WriteLine("═══════════════════════════════════");
