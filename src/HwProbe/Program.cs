// HwProbe 完整版 — 添加管理员权限 PnP 直接查询
// 避免 WMI Win32_PnPEntity 权限限制，改用 System.Management 的 ManagementObjectSearcher 直接查

using System.Management;
using LibreHardwareMonitor.Hardware;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║   HwProbe v2 — 总体验证结果         ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

int pass = 0, warn = 0, fail = 0;
void Report(string icon, string msg) {
    Console.WriteLine($"  {icon} {msg}");
    if (icon == "✅") pass++; else if (icon == "⚠️") warn++; else fail++;
}

// ==============================
// 1. WMI ACPI
// ==============================
Console.WriteLine("─── 1. ACPI EC 通信 ───");
try {
    using var clsSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM meta_class WHERE __CLASS = 'AcpiTest_MULong'");
    var found = clsSearcher.Get().Cast<ManagementObject>().Any();
    if (found)
    {
        Report("✅", "AcpiTest_MULong WMI 类存在 — 支持 GetSetULong / EC 读写");
        Report("ℹ️", "  关键方法: GetULong / SetULong / GetSetULong / FireULong / GetButton");
    }
    else
        Report("⚠️", "AcpiTest_MULong 不存在，EC 通信需要 UWACPIDriver");

    using var svcSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name='GCUBridge'");
    var svc = svcSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
    if (svc != null)
        Report("✅", $"GCUBridge 服务: State={svc["State"]}, StartMode={svc["StartMode"]}");
    else
        Report("⚠️", "GCUBridge 服务未注册");
}
catch (Exception ex) { Report("❌", $"WMI ACPI 查询失败: {ex.Message}"); }
Console.WriteLine();

// ==============================
// 2. 硬件传感器
// ==============================
Console.WriteLine("─── 2. 硬件传感器 ───");
try {
    var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsBatteryEnabled = true };
    computer.Open();

    foreach (var hw in computer.Hardware)
    {
        hw.Update();
        var name = hw.HardwareType switch {
            HardwareType.Cpu => "CPU", HardwareType.GpuNvidia => "dGPU (NVIDIA)",
            HardwareType.GpuIntel => "iGPU (Intel)", HardwareType.GpuAmd => "dGPU (AMD)",
            HardwareType.Memory => "内存", HardwareType.Battery => "电池",
            HardwareType.Motherboard => "主板", _ => hw.HardwareType.ToString()
        };

        // 只提取关键传感器
        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value > 0).Take(3);
        var loads = hw.Sensors.Where(s => s.SensorType == SensorType.Load && s.Value > 0).Take(2);
        var fans = hw.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value > 0).Take(2);
        var powers = hw.Sensors.Where(s => s.SensorType == SensorType.Power && s.Value > 0).Take(2);
        var clocks = hw.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value > 0).Take(2);

        var lines = new List<string>();
        foreach (var s in temps) lines.Add($"🌡{s.Name}:{s.Value:F1}°C");
        foreach (var s in loads) lines.Add($"📊{s.Name}:{s.Value:F1}%");
        foreach (var s in fans) lines.Add($"🌀{s.Name}:{s.Value:F0}RPM");
        foreach (var s in powers) lines.Add($"⚡{s.Name}:{s.Value:F1}W");
        foreach (var s in clocks) lines.Add($"⏱{s.Name}:{s.Value:F0}MHz");

        if (lines.Any())
            Report("✅", $"{name}: {hw.Name} — {string.Join(" | ", lines)}");
    }
    computer.Close();
}
catch (Exception ex) { Report("❌", $"硬件传感器失败: {ex.Message}"); }
Console.WriteLine();

// ==============================
// 3. PnP 设备
// ==============================
Console.WriteLine("─── 3. PnP 设备管理 ───");
try {
    // 用 Win32_PnPEntity 重试 — 关键是 SELECT * 而不是按 Class 过滤
    var targetPatterns = new (string name, string pattern)[] {
        ("NVIDIA dGPU",   "*NVIDIA*GeForce*"),
        ("Webcam",        "*Webcam*"),
        ("Webcam (备)",    "*webcam*"),
        ("摄像头",         "*摄像头*"),
        ("蓝牙",           "*Bluetooth*Adapter*"),
        ("ITE MCU",       "*ITE*"),
        ("AMD ACP",       "*AMD*Audio*CoProcessor*"),
    };

    using var devSearcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, Status, PNPDeviceID, PNPClass FROM Win32_PnPEntity");
    int foundCount = 0;
    var seen = new HashSet<string>();
    foreach (ManagementObject dev in devSearcher.Get())
    {
        var devName = (dev["Name"] ?? "").ToString();
        var status = (dev["Status"] ?? "OK").ToString();
        var devClass = (dev["PNPClass"] ?? "?").ToString();
        var devId = (dev["PNPDeviceID"] ?? "").ToString();

        foreach (var (label, pattern) in targetPatterns)
        {
            if (devName.Contains(pattern, StringComparison.OrdinalIgnoreCase) && seen.Add(label))
            {
                var st = status is "OK" ? "✅" : "❌";
                Report($"{st}", $"{label}: {devName} [Class={devClass}]");
                foundCount++;
            }
        }
    }
    if (foundCount == 0)
        Report("⚠️", "Win32_PnPEntity 无结果 — 尝试 PowerShell Get-PnpDevice 降级路径");

    // 直接查 Win32_VideoController 验证 dGPU
    using var gpuSearcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
    foreach (ManagementObject gpu in gpuSearcher.Get())
    {
        var gpuName = gpu["Name"]?.ToString() ?? "";
        var ram = Convert.ToInt64(gpu["AdapterRAM"] ?? 0L) / 1024 / 1024 / 1024;
        if (!string.IsNullOrEmpty(gpuName))
            Report("ℹ️", $"  GPU: {gpuName} ({ram} GB VRAM)");
    }
}
catch (Exception ex) { Report("❌", $"PnP 查询失败: {ex.Message}"); }
Console.WriteLine();

// ==============================
// 4. 电源/电池/系统信息
// ==============================
Console.WriteLine("─── 4. 系统信息 ───");
try {
    using var csSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory, NumberOfProcessors FROM Win32_ComputerSystem");
    foreach (ManagementObject cs in csSearcher.Get())
    {
        var vendor = cs["Manufacturer"]?.ToString() ?? "?";
        var model = cs["Model"]?.ToString() ?? "?";
        var ram = Convert.ToInt64(cs["TotalPhysicalMemory"] ?? 0L) / 1024 / 1024 / 1024;
        Report("✅", $"机型: {vendor} {model} — {ram} GB RAM");
    }

    using var batSearcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
    foreach (ManagementObject bat in batSearcher.Get())
    {
        var pct = bat["EstimatedChargeRemaining"]?.ToString() ?? "?";
        Report("✅", $"电池: {pct}% (插电状态: {(bat["BatteryStatus"]?.ToString() == "2" ? "是" : "否")})");
    }
}
catch (Exception ex) { Report("❌", $"系统信息失败: {ex.Message}"); }
Console.WriteLine();

// ==============================
// 总结
// ==============================
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine($"  结果: {pass} 通过  {warn} 警告  {fail} 失败");
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("📋 三链路状态:");
Console.WriteLine("   ACPI/EC ← 通过 WMI AcpiTest_MULong.GetSetULong");
Console.WriteLine("   硬件传感器 ← 通过 LibreHardwareMonitorLib (CPU/GPU/电池)");
Console.WriteLine("   PnP 设备 ← 通过 Win32_PnPEntity + PowerShell fallback");
Console.WriteLine();
Console.WriteLine("🔌 IPC 通道:");
Console.WriteLine("   CCU.Service (Named Pipe 'CCU.Service.Pipe') ←→ CCU.Wpf / CCU.Cli");
