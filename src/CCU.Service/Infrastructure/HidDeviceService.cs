using System.ComponentModel;
using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// USB HID 设备通信服务 — 键盘 RGB / 灯条控制
///
/// 基于对原厂 GCUService 中 HIDManager 和 HIDKeyboard 的逆向分析。
/// ITE 键盘 MCU: VID=0x048D, UsagePages: 0xFF02/0xFF03/0xFF12
/// 通信方式: HidD_SetOutputReport + HidD_GetFeature
/// </summary>
public class HidDeviceService : IDisposable
{
    private readonly ILogger<HidDeviceService> _logger;
    private HidDevice? _keyboardDevice;
    private HidStream? _keyboardStream;

    // ITE 键盘标准参数
    public const int ITE_VID = 0x048D;
    public const int ITE_USAGE_PAGE_4ZONE = 0xFF12;
    public const int ITE_USAGE_PAGE_ME_1ST = 0xFF02;
    public const int ITE_USAGE_PAGE_ME_2ND = 0xFF03;
    public const int ITE_USAGE_PAGE_LIGHTBAR = 0xFF03;
    public const int ITE_USAGE = 0x0001;

    // 灯效常量 (从原厂 ITE_SPEC)
    public const byte EFFECT_STATIC = 1;
    public const byte EFFECT_BREATHING = 2;
    public const byte EFFECT_WAVE = 3;
    public const byte EFFECT_REACTIVE = 4;
    public const byte EFFECT_RAINBOW = 5;
    public const byte EFFECT_RIPPLE = 6;
    public const byte EFFECT_NOMO = 8;
    public const byte EFFECT_MARQUEE = 9;
    public const byte EFFECT_RAINDROP = 10;
    public const byte EFFECT_STACK = 12;
    public const byte EFFECT_IMPACT = 13;
    public const byte EFFECT_AURORA = 14;
    public const byte EFFECT_NEON = 15;
    public const byte EFFECT_SPARK = 17;
    public const byte EFFECT_FLASH = 18;
    public const byte EFFECT_MIX = 19;
    public const byte EFFECT_GAMING = 21;
    public const byte EFFECT_RIPPLEO = 22;
    public const byte EFFECT_MUSIC = 34;

    // 控制字节
    public const byte CONTROL_LED_OFF = 1;
    public const byte CONTROL_LED_DEFAULT = 2;
    public const byte CONTROL_LED_WELCOME = 3;
    public const byte CONTROL_LED_NIGHT = 4;

    public const byte NV_SAVE = 1;
    public const byte NV_NOT_SAVE = 0;

    public HidDeviceService(ILogger<HidDeviceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 搜索并打开 ITE 键盘 HID 设备
    /// </summary>
    public bool Initialize(int vid = ITE_VID, int usagePage = ITE_USAGE_PAGE_ME_1ST)
    {
        try
        {
            var deviceList = DeviceList.Local;
            var devices = deviceList.GetHidDevices(vendorID: vid);

            foreach (var device in devices)
            {
                if (!device.TryOpen(out var stream))
                {
                    _keyboardDevice = device;
                    _keyboardStream = stream;
                    _logger.LogInformation("Opened keyboard HID device: {Product}", device.GetFriendlyName());
                    return true;
                }
            }

            _logger.LogWarning("No ITE keyboard HID device found (VID=0x{Vid:X4})", vid);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize keyboard HID");
            return false;
        }
    }

    /// <summary>
    /// 发送 Output Report 到键盘
    /// 这是写键盘设置的主要方法
    /// </summary>
    public bool SendOutputReport(byte[] report)
    {
        if (_keyboardStream == null)
            return false;

        try
        {
            _keyboardStream.Write(report);
            _logger.LogTrace("Sent HID report: {Report}", BitConverter.ToString(report));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send HID output report");
            return false;
        }
    }

    /// <summary>
    /// 获取 Feature Report (读取键盘当前状态)
    /// </summary>
    public byte[]? GetFeatureReport(int reportId = 0, int length = 64)
    {
        if (_keyboardStream == null)
            return null;

        try
        {
            byte[] buffer = new byte[length];
            buffer[0] = (byte)reportId;
            _keyboardStream.GetFeature(buffer);
            return buffer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get HID feature report");
            return null;
        }
    }

    /// <summary>
    /// 构建并发送设置灯效的命令
    ///
    /// 命令格式 (基于原厂 ITE 协议):
    ///   Byte 0: Report ID (通常 0x09)
    ///   Byte 1: 控制码 (0=运行效果, 1=关闭, 2=默认, 3=欢迎, 4=夜间)
    ///   Byte 2: 灯效类型
    ///   Byte 3: 速度 (1/3/5/7/9 → 快/中快/中/中慢/慢)
    ///   Byte 4: 亮度等级 (0-4)
    ///   Byte 5: 方向
    ///   Byte 6+: 颜色数据 (RGB 三元组, 数量依设备而定)
    /// </summary>
    public bool SetEffect(byte controlCode, byte effect, byte speed, byte brightness,
        byte direction, byte[]? colorData = null)
    {
        const int maxReportSize = 65; // 典型 HID 报告大小
        byte[] report = new byte[maxReportSize];

        report[0] = 0x09;           // Report ID (0x09 = SetEffect command)
        report[1] = controlCode;    // 控制码
        report[2] = effect;         // 灯效
        report[3] = TranslateSpeed(speed);
        report[4] = brightness;
        report[5] = direction;

        if (colorData != null)
        {
            int copyLen = Math.Min(colorData.Length, maxReportSize - 6);
            Array.Copy(colorData, 0, report, 6, copyLen);
        }

        return SendOutputReport(report);
    }

    public bool SetBrightness(byte brightness)
    {
        var current = GetFeatureReport();
        if (current == null) return false;
        return SetEffect(current[1], current[2], current[3], brightness, current[5]);
    }

    /// <summary>
    /// 速度值翻译: UI 速度 (1-10) → ITE 内部速度值
    /// ITE 映射: 10=快, 7=中快, 5=中, 3=中慢, 1=慢
    /// </summary>
    private byte TranslateSpeed(byte uiSpeed)
    {
        return uiSpeed switch
        {
            >= 10 => 1,
            >= 7 => 3,
            >= 5 => 5,
            >= 3 => 7,
            _ => 10
        };
    }

    public void Dispose()
    {
        _keyboardStream?.Dispose();
        _keyboardDevice = null;
    }
}
