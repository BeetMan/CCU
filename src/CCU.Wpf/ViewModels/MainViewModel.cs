using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CCU.Shared.IPC;
using CCU.Wpf.Services;
using CCU.Wpf.Views;

namespace CCU.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CcuIpcService _ipc;
    private readonly DispatcherTimer _pollTimer;

    public MainViewModel()
    {
        _ipc = new CcuIpcService();
        _ = _ipc.ConnectAsync();

        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => _ = PollHardwareAsync(), Dispatcher.CurrentDispatcher);
        _pollTimer.Start();

        // 初始化导航
        NavigateCommand = new RelayCommand<string>(Navigate);
        _views = new Dictionary<string, object>
        {
            ["performance"] = new Views.PerformanceView { DataContext = new PerformanceViewModel() },
            ["gpu"] = new Views.GpuView { DataContext = new GpuViewModel() },
            ["fan"] = new Views.FanView { DataContext = new FanViewModel() },
            ["keyboard"] = new Views.KeyboardView { DataContext = new KeyboardViewModel() },
            ["display"] = new Views.DisplayView { DataContext = new DisplayViewModel() },
            ["devices"] = new Views.DeviceView { DataContext = new DeviceViewModel() },
            ["settings"] = new Views.SettingsView { DataContext = new SettingsViewModel() },
            ["demo"] = new Views.DemoView { DataContext = new DemoViewModel() }
        };
        CurrentView = _views["performance"];
    }

    // ========================
    // 硬件信息
    // ========================
    [ObservableProperty] private double _cpuTemp;
    [ObservableProperty] private double _gpuTemp;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private double _cpuFanSpeed;
    [ObservableProperty] private double _gpuFanSpeed;
    [ObservableProperty] private double _cpuPower;
    [ObservableProperty] private double _gpuPower;
    [ObservableProperty] private double _cpuFreq;
    [ObservableProperty] private double _memoryUsage;
    [ObservableProperty] private double _batteryLevel;

    // ========================
    // 当前激活的性能模式 (0=office, 1=gaming, 2=turbo, 3=custom)
    // ========================
    [ObservableProperty] private int _activePerformanceMode;

    // ========================
    // 导航
    // ========================
    [ObservableProperty] private string _activeNav = "performance";
    [ObservableProperty] private object _currentView;
    private readonly Dictionary<string, object> _views;

    public ICommand NavigateCommand { get; }

    private void Navigate(string? page)
    {
        if (page == null || !_views.ContainsKey(page)) return;
        ActiveNav = page;
        CurrentView = _views[page];
    }

    // ========================
    // 全局操作
    // ========================
    [RelayCommand]
    private void SetPerformanceMode(string mode)
    {
        ActiveNav = mode switch
        {
            "office" => "performance", "gaming" => "performance", "turbo" => "performance", "custom" => "performance",
            _ => ActiveNav
        };

        // 立即更新 UI，不等 IPC 返回
        ActivePerformanceMode = ModeToInt(mode);
        App.NotifyModeChanged(ModeToInt(mode));

        // IPC 异步发送，不阻塞 UI
        _ = _ipc.SendCommandAsync(IpcMessageType.SetPerformanceMode, new { Mode = ModeToInt(mode) });
    }

    [RelayCommand]
    private static void MinimizeToTray() => Application.Current.MainWindow!.Hide();

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();

    private async Task PollHardwareAsync()
    {
        try
        {
            var info = await _ipc.SendAsync<HardwareInfoDto>(IpcMessageType.GetHardwareInfo, new { });
            if (info == null) return;

            CpuTemp = info.CpuTemperature;
            GpuTemp = info.GpuTemperature;
            CpuUsage = info.CpuUsage;
            GpuUsage = info.GpuUsage;
            CpuFanSpeed = info.CpuFanSpeed;
            GpuFanSpeed = info.GpuFanSpeed;
            CpuPower = info.CpuPower;
            GpuPower = info.GpuPower;
            CpuFreq = info.CpuFrequency;
            MemoryUsage = info.MemoryUsage;
            BatteryLevel = info.BatteryLevel;
        }
        catch (Exception ex)
        {
            // IPC 失败时静默处理 — 旧值保持不变
            System.Diagnostics.Debug.WriteLine($"PollHardwareAsync: {ex.Message}");
        }
    }

    private static int ModeToInt(string m) => m.ToLower() switch
    { "office" => 0, "gaming" => 1, "turbo" => 2, "custom" => 3, _ => 0 };
}

// ==============================================
// 性能模式页 ViewModel
// ==============================================
public partial class PerformanceViewModel : ObservableObject
{
    [ObservableProperty] private int _activeMode;

    [RelayCommand]
    private async Task SetMode(int mode) => ActiveMode = mode;
}

// ==============================================
// GPU 页 ViewModel
// ==============================================
public partial class GpuViewModel : ObservableObject
{
    [ObservableProperty] private int _gpuMode = 2; // hybrid default
    [ObservableProperty] private double _coreOffset;
    [ObservableProperty] private double _memOffset;
    [ObservableProperty] private double _thermalTarget = 87;
    [ObservableProperty] private bool _whisperMode;
    [ObservableProperty] private int _panelHz = 0; // 0=系统默认

    public GpuViewModel()
    {
        // 在构造函数中检测 NVIDIA dGPU 是否被禁用
        _ = DetectGpuStateAsync();
    }

    private async Task DetectGpuStateAsync()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\cimv2", "SELECT Status FROM Win32_PnPEntity WHERE Name LIKE '%NVIDIA%GeForce%'");
            var results = searcher.Get().Cast<System.Management.ManagementObject>().ToList();
            if (results.Count > 0)
            {
                var status = results[0]["Status"]?.ToString();
                // Status=OK → dGPU 可用 → 不是 iGPU only
                // Status=Error → dGPU 被禁用 → 是 iGPU only
                bool dgpuEnabled = status is "OK" or "";
                // 注意: 这只能区分 iGPU only vs 其他模式
                // 无法区分 Hybrid vs dGPU only vs HotSwap (需要 EC)
                if (!dgpuEnabled)
                    GpuMode = 0; // iGPU only
                System.Diagnostics.Debug.WriteLine($"GpuViewModel: dGPU Status={status}, GpuMode={GpuMode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GpuViewModel DetectGpuState: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SetGpuMode(int mode) => GpuMode = mode;
}

// FanViewModel moved to FanViewModel.cs

// ==============================================
// 键盘 RGB ViewModel
// ==============================================
public partial class KeyboardViewModel : ObservableObject
{
    [ObservableProperty] private int _effect = 5; // rainbow
    [ObservableProperty] private int _speed = 5;
    [ObservableProperty] private int _brightness = 3;
    [ObservableProperty] private byte _colorR = 0;
    [ObservableProperty] private byte _colorG = 212;
    [ObservableProperty] private byte _colorB = 170;
    [ObservableProperty] private bool _lightbarEnabled = true;

    // 可用效果列表
    public static readonly (string Name, int Id)[] Effects =
    {
        ("关", 0), ("静态", 1), ("呼吸", 2), ("波浪", 3),
        ("响应", 4), ("彩虹", 5), ("涟漪", 6), ("雨滴", 10),
        ("霓虹", 15), ("跑马灯", 9), ("极光", 14), ("音乐", 34),
        ("游戏联动", 21), ("火花", 17), ("闪烁", 18), ("混合", 19)
    };

    [RelayCommand]
    private void SetEffect(int id) => Effect = id;
}

// ==============================================
// 显示 ViewModel
// ==============================================
public partial class DisplayViewModel : ObservableObject
{
    [ObservableProperty] private int _profile = 0; // vibrant
    [ObservableProperty] private double _brightness = 50;
    [ObservableProperty] private double _colorTemp = 6500;
    [ObservableProperty] private double _saturation = 1.5;
    [ObservableProperty] private double _contrast = 1.0;
    [ObservableProperty] private double _gamma = 1.0;

    [RelayCommand]
    private void SetProfile(int p) => Profile = p;
}

// ==============================================
// 设备开关 ViewModel
// ==============================================
public partial class DeviceViewModel : ObservableObject
{
    [ObservableProperty] private bool _webcam = true;
    [ObservableProperty] private bool _dgpu = true;
    [ObservableProperty] private bool _amdAcp = true;
    [ObservableProperty] private bool _bluetooth = true;
    [ObservableProperty] private bool _airplaneMode;

    public DeviceViewModel()
    {
        _ = DetectDeviceStatesAsync();
    }

    private async Task DetectDeviceStatesAsync()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\cimv2", "SELECT Name, Status FROM Win32_PnPEntity");
            foreach (System.Management.ManagementObject dev in searcher.Get())
            {
                var name = (dev["Name"] ?? "").ToString();
                var status = (dev["Status"] ?? "OK").ToString();
                bool ok = status is "OK" or "";

                if (name.Contains("*webcam*", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("*Webcam*", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("*Camera*", StringComparison.OrdinalIgnoreCase))
                {
                    Webcam = ok;
                }
                if (name.Contains("*NVIDIA*", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("*GeForce*", StringComparison.OrdinalIgnoreCase))
                {
                    Dgpu = ok;
                }
                if (name.Contains("*AMD Audio CoProcessor*", StringComparison.OrdinalIgnoreCase))
                {
                    AmdAcp = ok;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeviceViewModel DetectDeviceStates: {ex.Message}");
        }
    }

    partial void OnWebcamChanged(bool value) => SendSwitch("webcam", value);
    partial void OnDgpuChanged(bool value) => SendSwitch("dgpu", value);
    partial void OnAmdAcpChanged(bool value) => SendSwitch("amdacp", value);

    private async void SendSwitch(string device, bool enable)
    {
        var ipc = new CcuIpcService();
        await ipc.ConnectAsync();
        await ipc.SendCommandAsync(IpcMessageType.SetDeviceSwitch, new { Device = device, Enable = (bool?)enable });
    }
}

// ==============================================
// 设置 ViewModel
// ==============================================
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _osdEnabled = true;
    [ObservableProperty] private string _language = "zh-cn";
    [ObservableProperty] private string _theme = "dark";
}
