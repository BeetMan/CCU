using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CCU.Shared.IPC;
using CCU.Shared.Models;
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
        var perfVm = new PerformanceViewModel();
        _views = new Dictionary<string, object>
        {
            ["performance"] = new Views.PerformanceView { DataContext = perfVm },
            ["gpu"] = new Views.GpuView { DataContext = new GpuViewModel() },
            ["fan"] = new Views.FanView { DataContext = _fanViewModel },
            ["keyboard"] = new Views.KeyboardView { DataContext = new KeyboardViewModel(_ipc) },
            ["display"] = new Views.DisplayView { DataContext = new DisplayViewModel() },
            ["devices"] = new Views.DeviceView { DataContext = new DeviceViewModel() },
            ["demo"] = new Views.DemoView { DataContext = new DemoViewModel() }
        };
        CurrentView = _views["performance"];

        // 异步拉取自定义 Profile 目录 + 风扇曲线
        _ = perfVm.LoadCatalogAsync(_ipc);
        _ = _fanViewModel.LoadCurveAsync(_ipc);

        // 设置页：应用绑定管理
        _settingsViewModel = new SettingsViewModel(_ipc);
        _views["settings"] = new Views.SettingsView { DataContext = _settingsViewModel };
    }

    private readonly SettingsViewModel _settingsViewModel;
    private readonly FanViewModel _fanViewModel = new();

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
    [ObservableProperty] private string _modeLabel = "--";
    [ObservableProperty] private bool _fanBoostOn;
    [ObservableProperty] private int _gpuOcOffset;

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

        var request = ModeToPayload(mode);
        ActivePerformanceMode = (int)(request.GetType().GetProperty("Mode")?.GetValue(request) ?? 0);
        App.NotifyModeChanged(ActivePerformanceMode);

        // IPC 异步发送，不阻塞 UI
        _ = _ipc.SendCommandAsync(IpcMessageType.SetPerformanceMode, request);
    }

    [RelayCommand]
    private void SetTurboDetail(string detail)
    {
        var silent = detail == "silent";
        ActivePerformanceMode = 2;
        _ = _ipc.SendCommandAsync(IpcMessageType.SetPerformanceMode,
            new { Mode = 2, Silent = (int?)(silent ? 1 : 0), Extreme = (int?)(silent ? 0 : 1) });
    }

    [RelayCommand]
    private void SetCustomProfile(string slot)
    {
        if (!int.TryParse(slot, out var slotNumber) || slotNumber < 1) return;
        ActivePerformanceMode = 3;
        _ = _ipc.SendCommandAsync(IpcMessageType.SetPerformanceMode,
            new { Mode = 3, ProfileIndex = slotNumber - 1 });
    }

    [RelayCommand]
    private void ToggleFanBoost()
    {
        var target = !FanBoostOn;
        FanBoostOn = target; // 乐观更新，轮询会纠正
        _ = _ipc.SendCommandAsync(IpcMessageType.SetFanBoost, new { Enable = target });
    }

    [RelayCommand]
    private void SetTurboOc(string state)
    {
        var offset = state == "on" ? 150 : 0;
        GpuOcOffset = offset;
        _ = _ipc.SendCommandAsync(IpcMessageType.SetTurboOc, new { Offset = offset });
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

            // 真实模式状态以服务轮询结果为准（乐观更新后由它纠正）
            if (info.OperatingMode >= 0)
            {
                ActivePerformanceMode = info.OperatingMode;
                ModeLabel = string.IsNullOrWhiteSpace(info.ModeLabel) ? "--" : info.ModeLabel;
            }
            FanBoostOn = info.FanBoostEnabled == 1;
            GpuOcOffset = info.TurboGpuOcOffset;

            // 同步到性能页 ViewModel
            if (CurrentView is Views.PerformanceView pv && pv.DataContext is PerformanceViewModel perfVm)
            {
                perfVm.ActiveMode = ActivePerformanceMode;
                perfVm.FanBoostOn = FanBoostOn;
                perfVm.GpuOcOffset = GpuOcOffset;
            }
        }
        catch (Exception ex)
        {
            // IPC 失败时静默处理 — 旧值保持不变
            System.Diagnostics.Debug.WriteLine($"PollHardwareAsync: {ex.Message}");
        }
    }

    private static int ModeToInt(string m) => m.ToLower() switch
    { "office" => 0, "gaming" => 1, "turbo" => 2, "custom" => 3, _ => 0 };

    private static object ModeToPayload(string mode)
    {
        var value = mode.ToLowerInvariant();
        if (value.StartsWith("custom:"))
        {
            var slot = int.TryParse(value[7..], out var s) ? Math.Max(0, s - 1) : 0;
            return new { Mode = 3, ProfileIndex = (int?)slot };
        }
        return value switch
        {
            "office" => (object)new { Mode = 0 },
            "gaming" => new { Mode = 1 },
            "turbo:silent" => new { Mode = 2, Silent = (int?)1, Extreme = (int?)0 },
            "turbo" or "turbo:extreme" => new { Mode = 2, Silent = (int?)0, Extreme = (int?)1 },
            "custom" => new { Mode = 3, ProfileIndex = (int?)0 },
            _ => new { Mode = 0 }
        };
    }
}

// ==============================================
// 性能模式页 ViewModel
// ==============================================
public partial class PerformanceViewModel : ObservableObject
{
    [ObservableProperty] private int _activeMode;
    [ObservableProperty] private bool _fanBoostOn;
    [ObservableProperty] private int _gpuOcOffset;

    /// <summary>已启用的自定义 Profile 芯片（服务目录发现）</summary>
    public ObservableCollection<CustomProfileChip> CustomProfiles { get; } = [];

    [RelayCommand] private async Task SetMode(int mode) => ActiveMode = mode;

    /// <summary>从服务拉取自定义 Profile 目录（不阻塞 UI）</summary>
    public async Task LoadCatalogAsync(Services.CcuIpcService ipc)
    {
        var resp = await ipc.SendAsync<ModeCatalogDto>(IpcMessageType.GetModeCatalog, new { });
        if (resp?.Catalog == null) return;
        CustomProfiles.Clear();
        foreach (var m in resp.Catalog.Where(c => c.OperatingMode == 3)
                     .OrderBy(c => c.ProfileIndex))
        {
            CustomProfiles.Add(new CustomProfileChip(m.Label, (m.ProfileIndex ?? 0) + 1));
        }
    }
}

public sealed record CustomProfileChip(string Label, int Slot);

public sealed class ModeCatalogEntry
{
    public string Label { get; set; } = "";
    public string Action { get; set; } = "";
    public int OperatingMode { get; set; }
    public int? ProfileIndex { get; set; }
    public int? Silent { get; set; }
    public int? Extreme { get; set; }
}

public sealed class ModeCatalogDto
{
    public bool Success { get; set; }
    public List<ModeCatalogEntry>? Catalog { get; set; }
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
// 键盘 RGB ViewModel — 原厂 MQTT 灯效协议
// ==============================================
public partial class KeyboardViewModel : ObservableObject
{
    private readonly CcuIpcService _ipc;
    private readonly DispatcherTimer _debounce;

    // 灯效名 = 原厂 RGBKB_Effect 枚举（反编译源码确认）
    public static readonly (string Name, string VendorEffect)[] Effects =
    {
        ("关", ""),
        ("静态", "Single"),
        ("呼吸", "Breathing"),
        ("波浪", "Wave"),
        ("彩色波浪", "ColorfulWave"),
        ("响应", "Reactive"),
        ("彩虹", "Rainbow"),
        ("演漪", "Ripple"),
        ("雨滴", "Raindrop"),
        ("跑马灯", "Marquee"),
        ("冲击", "Impact"),
        ("火花", "Spark"),
        ("极光", "Aurora"),
        ("音乐联动", "Music"),
        ("游戏联动", "Gaming"),
        ("闪烁", "Flash"),
        ("混合", "Mix"),
        ("霓虹闪烁", "Twinkling"),
        ("黎明", "Dawn"),
        ("正弦", "Sine"),
        ("交错", "Interlace"),
        ("对角", "Diagonal"),
    };

    [ObservableProperty] private string _effectName = "Single";
    [ObservableProperty] private int _speed = 2;
    [ObservableProperty] private int _brightness = 4; // 原厂等级 0-4 (25% 步进)
    [ObservableProperty] private byte _colorR = 0;
    [ObservableProperty] private byte _colorG = 212;
    [ObservableProperty] private byte _colorB = 170;
    [ObservableProperty] private bool _lightbarEnabled = true;

    public KeyboardViewModel(CcuIpcService ipc)
    {
        _ipc = ipc;
        // 色彩/亮度/速度调整 400ms 防抖后重发当前效果
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = ApplyAsync(); };
    }

    public bool PowerOn => EffectName != "" && Brightness > 0;

    [RelayCommand]
    private void SetEffect(string name)
    {
        EffectName = name;
        _ = ApplyAsync();
    }

    partial void OnSpeedChanged(int value) => RestartDebounce();
    partial void OnBrightnessChanged(int value) => RestartDebounce();
    partial void OnColorRChanged(byte value) => RestartDebounce();
    partial void OnColorGChanged(byte value) => RestartDebounce();
    partial void OnColorBChanged(byte value) => RestartDebounce();

    partial void OnLightbarEnabledChanged(bool value)
    {
        _ = _ipc.SendCommandAsync(IpcMessageType.SetLogoLight,
            new { R = (int)ColorR, G = (int)ColorG, B = (int)ColorB,
                  Brightness = Brightness, Power = value });
    }

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task ApplyAsync()
    {
        var effect = PowerOn ? EffectName : null;
        await _ipc.SendCommandAsync(IpcMessageType.SetKeyboardEffect, new
        {
            Effect = effect,
            R = (int)ColorR,
            G = (int)ColorG,
            B = (int)ColorB,
            Brightness = Math.Clamp(Brightness, 0, 4),
            Speed = Math.Clamp(Speed, 1, 5),
            Power = PowerOn
        });

        // Logo 灯跟随同色
        if (LightbarEnabled)
        {
            await _ipc.SendCommandAsync(IpcMessageType.SetLogoLight, new
            {
                R = (int)ColorR, G = (int)ColorG, B = (int)ColorB,
                Brightness = Math.Clamp(Brightness, 0, 4), Power = PowerOn
            });
        }
    }
}

// ==============================================
// 显示 ViewModel
// ==============================================
public partial class DisplayViewModel : ObservableObject
{
    [ObservableProperty] private double _brightness = 50;
    [ObservableProperty] private ObservableCollection<string> _refreshRates = [];
    [ObservableProperty] private string _selectedRefreshRate = "";
    [ObservableProperty] private string _statusText = "";
    private bool _suppressRateUpdate;

    public DisplayViewModel()
    {
        _ = LoadDisplayStateAsync();
    }

    partial void OnBrightnessChanged(double value)
    {
        // 拖动结束前不频繁写 WMI（WmiSetBrightness 每次 50ms+）
        if (_suppressRateUpdate) return;
        _suppressRateUpdate = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            SetBrightnessWmi(Brightness);
            _suppressRateUpdate = false;
        });
    }

    partial void OnSelectedRefreshRateChanged(string value)
    {
        if (_suppressRateUpdate || string.IsNullOrEmpty(value)) return;
        ApplyRefreshRate(value);
    }

    /// <summary>读取当前亮度 + 可用刷新率（标准系统 API，安全）</summary>
    private async Task LoadDisplayStateAsync()
    {
        try
        {
            Brightness = await Task.Run(ReadBrightnessWmi);
        }
        catch { Brightness = 50; }

        try
        {
            var rates = await Task.Run(ListRefreshRates);
            _suppressRateUpdate = true;
            RefreshRates.Clear();
            foreach (var r in rates) RefreshRates.Add(r);
            SelectedRefreshRate = rates.Count > 0 ? rates[^1] : ""; // 默认最高
            _suppressRateUpdate = false;
        }
        catch (Exception ex)
        {
            StatusText = $"刷新率枚举失败: {ex.Message}";
        }
    }

    private static double ReadBrightnessWmi()
    {
        using var searcher = new System.Management.ManagementObjectSearcher(
            @"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
        var obj = searcher.Get().Cast<System.Management.ManagementObject>().FirstOrDefault()
                  ?? throw new InvalidOperationException("内建显示器不支持 WMI 亮度");
        return Convert.ToDouble(obj["CurrentBrightness"]);
    }

    private static void SetBrightnessWmi(double value)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            var obj = searcher.Get().Cast<System.Management.ManagementObject>().FirstOrDefault();
            obj?.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)Math.Clamp(value, 0, 100) });
        }
        catch { /* 无背光控制器时静默 */ }
    }

    private static List<string> ListRefreshRates()
    {
        var rates = new SortedSet<int>();
        var current = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current))
            throw new InvalidOperationException("无法读取当前显示模式");

        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmPelsWidth == current.dmPelsWidth && dm.dmPelsHeight == current.dmPelsHeight)
            {
                rates.Add(dm.dmDisplayFrequency);
            }
        }
        return rates.Select(r => r + " Hz").ToList();
    }

    private void ApplyRefreshRate(string rateText)
    {
        try
        {
            if (!int.TryParse(rateText.Replace(" Hz", ""), out var hz)) return;
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm);
            dm.dmDisplayFrequency = hz;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            var result = ChangeDisplaySettings(ref dm, 0);
            StatusText = result == 0 ? $"已切换到 {hz} Hz" : $"切换失败 (code {result})";
        }
        catch (Exception ex)
        {
            StatusText = $"刷新率切换失败: {ex.Message}";
        }
    }

    // === user32 显示模式 P/Invoke ===
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSize;
        public short dmDriverVersion;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE dm);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);
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
                var name = dev["Name"]?.ToString() ?? "";
                var status = dev["Status"]?.ToString() ?? "OK";
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
    private readonly CcuIpcService _ipc;

    public SettingsViewModel(CcuIpcService ipc)
    {
        _ipc = ipc;
        _ = LoadAppBindingsAsync();
    }

    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _osdEnabled = true;
    [ObservableProperty] private string _language = "zh-cn";
    [ObservableProperty] private string _theme = "dark";

    // === 应用绑定自动切换 ===
    [ObservableProperty] private bool _appBindingEnabled;
    [ObservableProperty] private bool _restoreOnLeave = true;
    [ObservableProperty] private string _newProcess = "";
    [ObservableProperty] private int _newMode = 1;
    public ObservableCollection<AppProfileRow> AppProfiles { get; } = [];

    public static readonly (string Name, int Mode)[] ModeOptions =
    { ("办公", 0), ("游戏", 1), ("狂暴·极速", 2), ("狂暴·静技", 2) };

    /// <summary>新增绑定（进程名 + 模式）</summary>
    [RelayCommand]
    private async Task AddBinding()
    {
        var process = NewProcess.Trim();
        if (process.Length == 0) return;
        if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process += ".exe";

        var (mode, silent, extreme) = NewMode switch
        {
            0 => (0, (int?)null, (int?)null),
            1 => (1, null, null),
            2 => (2, (int?)0, (int?)1),
            3 => (2, (int?)1, (int?)0),
            _ => (1, null, null)
        };

        await _ipc.SendCommandAsync(IpcMessageType.SaveAppProfile,
            new { Process = process, Mode = mode, Silent = silent, Extreme = extreme, Label = "" });
        NewProcess = "";
        await LoadAppBindingsAsync();
    }

    [RelayCommand]
    private async Task DeleteBinding(string process)
    {
        await _ipc.SendCommandAsync(IpcMessageType.DeleteAppProfile, new { Process = process, Mode = 0 });
        await LoadAppBindingsAsync();
    }

    [RelayCommand]
    private async Task ToggleAppBinding()
    {
        await _ipc.SendCommandAsync(IpcMessageType.SetAppBindingEnabled,
            new { Enabled = AppBindingEnabled });
    }

    private async Task LoadAppBindingsAsync()
    {
        var resp = await _ipc.SendAsync<AppBindingDto>(IpcMessageType.GetModeCatalog, new { });
        if (resp?.AppBinding == null) return;
        AppBindingEnabled = resp.AppBinding.Enabled;
        RestoreOnLeave = resp.AppBinding.RestoreOnLeave;
        AppProfiles.Clear();
        foreach (var p in resp.AppBinding.Profiles)
        {
            AppProfiles.Add(new AppProfileRow(p.Process, p.DisplayName,
                p.Mode switch { 0 => "办公", 1 => "游戏", 2 => p.Silent == 1 ? "狂暴·静技" : "狂暴·极速", 3 => $"自定义#{(p.ProfileIndex ?? 0) + 1}", _ => "?" }));
        }
    }
}

public sealed class AppProfileDto
{
    public string Process { get; set; } = "";
    public int Mode { get; set; }
    public int? ProfileIndex { get; set; }
    public int? Silent { get; set; }
    public int? Extreme { get; set; }
    public string Label { get; set; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Process : Label;
}

public sealed class AppBindingDto
{
    public bool Success { get; set; }
    public List<ModeCatalogEntry>? Catalog { get; set; }
    public AppBindingStateDto? AppBinding { get; set; }
}

public sealed class AppBindingStateDto
{
    public bool Enabled { get; set; }
    public bool RestoreOnLeave { get; set; }
    public List<AppProfileDto> Profiles { get; set; } = [];
}

public sealed record AppProfileRow(string Process, string DisplayName, string ModeName);
