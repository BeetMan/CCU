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
    Console.WriteLine("⚠️ 当前不是 SYSTEM 账户 — EC 写入可能被 WMI 拒绝");
    Console.WriteLine("   安装为 Windows Service 后将以 LocalSystem 运行");
    Console.WriteLine("   命令: sc.exe create CCUService binPath= \"...\" obj= LocalSystem start= auto");
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

builder.Services.AddSingleton<PerformanceManager>();
builder.Services.AddSingleton<FanControlManager>();
builder.Services.AddSingleton<GpuManager>();
builder.Services.AddSingleton<DeviceSwitchManager>();

builder.Services.AddHostedService<GcuBackgroundService>();

var host = builder.Build();
host.Run();
