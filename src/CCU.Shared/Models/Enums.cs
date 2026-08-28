using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace CCU.Shared.Models;

/// <summary>
/// 性能模式枚举 — 对应 EC 中的 OperatingMode
/// </summary>
public enum PerformanceMode
{
    Office = 0,
    Gaming = 1,
    Turbo = 2,
    Custom = 3
}

/// <summary>
/// GPU 工作模式 (MUX Switch)
/// </summary>
public enum GpuMode
{
    /// <summary>仅集成显卡 (iGPU only / MSHybrid OFF)</summary>
    IgpuOnly = 0,
    /// <summary>仅独立显卡 (dGPU only)</summary>
    DgpuOnly = 1,
    /// <summary>混合模式 (Optimus / MSHybrid)</summary>
    Hybrid = 2,
    /// <summary>热切换 (Advanced Optimus)</summary>
    HotSwap = 3
}

/// <summary>
/// 风扇控制模式
/// </summary>
public enum FanControlMode
{
    Auto = 0,
    Manual = 1,
    /// <summary>最高转速 (Fn+1 / Boost)</summary>
    Max = 2
}

/// <summary>
/// 键盘灯效类型
/// </summary>
public enum KeyboardEffect
{
    Static = 1,
    Breathing = 2,
    Wave = 3,
    Reactive = 4,
    Rainbow = 5,
    Ripple = 6,
    Raindrop = 10,
    Neon = 15,
    Marquee = 9,
    Stack = 12,
    Impact = 13,
    Spark = 17,
    Aurora = 14,
    Music = 34,
    Gaming = 21,
    Flash = 18,
    Mix = 19,
    ColorfulWave = 28,
    Dawn = 29,
    ColorMarquee = 30,
    Twinkling = 31,
    Sine = 32,
    Interlace = 33,
    Diagonal = 34
}

/// <summary>
/// 显示颜色预设
/// </summary>
public enum DisplayColorProfile
{
    VibrantMode = 0,
    InternetMode = 1,
    VideoMode = 2,
    LowBlueMode = 3,
    CinemaMode = 4,
    PhotoMode = 5
}

/// <summary>
/// 硬件传感器读数
/// </summary>
public class HardwareInfo
{
    public float CpuTemperature { get; set; }
    public float GpuTemperature { get; set; }
    public float CpuUsage { get; set; }
    public float GpuUsage { get; set; }
    public float CpuFanSpeed { get; set; }
    public float GpuFanSpeed { get; set; }
    public float CpuPower { get; set; }
    public float GpuPower { get; set; }
    public float CpuFrequency { get; set; }
    public float GpuCoreFrequency { get; set; }
    public float GpuMemFrequency { get; set; }
    public float BatteryLevel { get; set; }
    public float MemoryUsage { get; set; }

    // === 原厂模式状态 (从智控中心配置文件只读解析, -1 = 未知) ===
    public int OperatingMode { get; set; } = -1;
    public int CustomProfileIndex { get; set; } = -1;
    public int TurboGpuOcOffset { get; set; }
    public int FanBoostEnabled { get; set; }
    public string ModeLabel { get; set; } = "";

    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 风扇曲线数据点
/// </summary>
public class FanCurvePoint
{
    /// <summary>温度上升触发阈值 (°C)</summary>
    public int UpTemperature { get; set; }
    /// <summary>温度下降触发阈值 (°C)</summary>
    public int DownTemperature { get; set; }
    /// <summary>风扇占空比 (0-100%)</summary>
    public int Duty { get; set; }
}

/// <summary>
/// 风扇曲线配置
/// </summary>
public class FanTable
{
    public string Name { get; set; } = "";
    public string ModelPrefix { get; set; } = "";
    public bool Activated { get; set; }
    public bool FanControlRespective { get; set; }
    public List<FanCurvePoint> CpuCurve { get; set; } = new();
    public List<FanCurvePoint> GpuCurve { get; set; } = new();
}

/// <summary>
/// 显示配置
/// </summary>
public class DisplaySettings
{
    public DisplayColorProfile ColorProfile { get; set; }
    public int Brightness { get; set; }
    public int ColorTemperature { get; set; }
    public double ColorR { get; set; }
    public double ColorG { get; set; }
    public double ColorB { get; set; }
    public double VibrantValue { get; set; }
    public double Contrast { get; set; }
    public double Gamma { get; set; }
    public int RefreshRate { get; set; }
}

/// <summary>
/// 设备开关状态
/// </summary>
public class DeviceSwitches
{
    public bool WebcamEnabled { get; set; } = true;
    public bool DGpuEnabled { get; set; } = true;
    public bool AmdAudioCoProcessorEnabled { get; set; } = true;
    public bool BluetoothEnabled { get; set; } = true;
    public bool WiFiEnabled { get; set; } = true;
}

/// <summary>
/// 应用绑定配置
/// </summary>
public class AppProfileBinding
{
    public string AppName { get; set; } = "";
    public string AppPath { get; set; } = "";
    public bool OfficeEnable { get; set; }
    public int OfficeProfileIndex { get; set; }
    public bool GameEnable { get; set; }
    public int GameProfileIndex { get; set; }
    public bool TurboEnable { get; set; }
    public int TurboProfileIndex { get; set; }
}

/// <summary>
/// 灯光效果数据
/// </summary>
public class LightingEffectData
{
    public KeyboardEffect Effect { get; set; }
    public byte Speed { get; set; } = 5;
    public byte Brightness { get; set; } = 3;
    public byte Direction { get; set; }
    public List<KeyboardColor> Colors { get; set; } = new();
}

public class KeyboardColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}
