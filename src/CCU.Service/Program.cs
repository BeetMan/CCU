using CCU.Service;
using CCU.Service.Core;
using CCU.Service.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog.Extensions.Logging;
using System.Diagnostics;
using System.Security.Principal;

// ============================================================
// CCU.Service — 确保以 SYSTEM 权限运行
// ============================================================

// 检查当前权限
var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
bool isSystem = identity.IsSystem;
bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

Console.WriteLine($"CCU.Service starting as: {identity.Name}");
Console.WriteLine($"  IsSystem: {isSystem}, IsAdmin: {isAdmin}");

if (!isSystem)
{
    Console.WriteLine("ℹ️ MQTT 优先架构下非 SYSTEM 也可运行（控制走 MQTT，状态读配置文件）。");
    Console.WriteLine("   仅 EC 诊断/研究支线需要 LocalSystem；当前硬件监控可能受限。");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CCUService";
});

builder.Logging.ClearProviders();

// 使用绝对路径加载 NLog.config — Windows Service 的工作目录是 %WinDir%\System32
var nlogConfig = Path.Combine(AppContext.BaseDirectory, "NLog.config");
if (!File.Exists(nlogConfig))
{
    // Fallback: 开发时可能从项目目录运行
    nlogConfig = Path.Combine(Directory.GetCurrentDirectory(), "NLog.config");
}
builder.Logging.AddNLog(nlogConfig);

builder.Services.AddSingleton<WmiAcpiClient>();
builder.Services.AddSingleton<KernelAcpiClient>();
builder.Services.AddSingleton<PnpDeviceController>();
builder.Services.AddSingleton<UefiVariableService>();
builder.Services.AddSingleton<HidDeviceService>();
builder.Services.AddSingleton<HardwareMonitorService>();

// MQTT 优先架构核心
builder.Services.AddSingleton<VendorStateReader>();
builder.Services.AddSingleton<VendorMqttControl>();

// EC 研究支线（写入默认未被 IPC 路由启用）
builder.Services.AddSingleton<PerformanceManager>();
builder.Services.AddSingleton<FanControlManager>();
builder.Services.AddSingleton<GpuManager>();
builder.Services.AddSingleton<DeviceSwitchManager>();

builder.Services.AddHostedService<GcuBackgroundService>();

var host = builder.Build();
host.Run();
