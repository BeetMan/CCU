using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 原厂灯光控制 — 经 GCUBridge MQTT。
///
/// 协议来源:
/// - 静态色命令: DynamicLightingBridge 实测验证 (SetEffectALL / effect=Single)
/// - 灯效词表: 原厂 CCUWinUI 反编译源码 RGBKB_Effect 枚举 (26 种)
/// - 亮度等级: 1-4 (25% 步进, PercentToVendorLevel)
/// - topic: Keyboard/Ctrl (键盘), HidLightbar_Logo/Ctrl (Logo 灯)
/// </summary>
public sealed class VendorLightingController
{
    private readonly VendorMqttControl _mqtt;
    private readonly ILogger<VendorLightingController> _logger;

    public VendorLightingController(VendorMqttControl mqtt, ILogger<VendorLightingController> logger)
    {
        _mqtt = mqtt;
        _logger = logger;
    }

    private const string KeyboardTopic = "Keyboard/Ctrl";
    private const string LogoTopic = "HidLightbar_Logo/Ctrl";

    /// <summary>原厂支持的灯效名 (RGBKB_Effect 枚举子集, 已排除 UserMode/Manual 等逐键协议)。</summary>
    public static readonly IReadOnlyList<string> SupportedEffects =
    [
        "Single", "Breathing", "Wave", "ColorfulWave", "Reactive", "Rainbow",
        "Ripple", "Raindrop", "Marquee", "Impact", "Spark", "Aurora",
        "Music", "Gaming", "Flash", "Mix", "Twinkling", "Dawn",
        "Sine", "Interlace", "Diagonal", "Thinking", "Devour"
    ];

    /// <summary>
    /// 设置键盘灯效。effect=null 表示纯亮度/开关调节（沿用最近效果由固件记忆，仅发亮度）。
    /// </summary>
    public void ApplyKeyboard(string effect, byte r, byte g, byte b, int brightnessLevel, int speed, bool on)
    {
        if (!on || brightnessLevel <= 0)
        {
            _mqtt.PublishTopic(KeyboardTopic, new { function = "SetPower", powerstatus = 0 });
            _logger.LogInformation("键盘灯已关闭");
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["function"] = "SetPower",
            ["powerstatus"] = 1,
        };
        _mqtt.PublishTopic(KeyboardTopic, payload);

        _mqtt.PublishTopic(KeyboardTopic, CreateEffectCommand(effect, r, g, b, brightnessLevel, speed));
        _logger.LogInformation("键盘灯效: {Effect} RGB({R},{G},{B}) 亮度{Level} 速度{Speed}",
            effect, r, g, b, brightnessLevel, speed);
    }

    /// <summary>Logo 灯 — 已验证的静态色命令。</summary>
    public void ApplyLogo(byte r, byte g, byte b, int brightnessLevel, bool on)
    {
        if (!on || brightnessLevel <= 0)
        {
            _mqtt.PublishTopic(LogoTopic, new { function = "SetPower", powerstatus = 0 });
            _logger.LogInformation("Logo 灯已关闭");
            return;
        }

        _mqtt.PublishTopic(LogoTopic, new { function = "SetPower", powerstatus = 1 });
        _mqtt.PublishTopic(LogoTopic, CreateStaticColor(r, g, b, brightnessLevel));
        _logger.LogInformation("Logo 灯: RGB({R},{G},{B}) 亮度{Level}", r, g, b, brightnessLevel);
    }

    /// <summary>
    /// 效果命令 — 结构与已验证的静态色命令一致，仅 effect/speed/direction 不同。
    /// </summary>
    private static object CreateEffectCommand(string effect, byte r, byte g, byte b, int level, int speed) => new
    {
        function = "SetEffectALL",
        mode = "Lighting",
        effect,
        light = level.ToString(),
        speed = Math.Clamp(speed, 1, 5).ToString(),
        direction = (string?)null,
        nv_save = (string?)null,
        color = CreateColorPayload(r, g, b),
        MonochromeIndex = "33",
        ManualIndex1 = "1",
        ManualIndex2 = "5",
        ManualIndex3 = "9",
        ManualIndex4 = "13",
        ManualIndex5 = "21",
        ManualIndex6 = "25",
        ManualInterval = "10",
        BreathingIndex = "1"
    };

    private static object CreateStaticColor(byte r, byte g, byte b, int level) => new
    {
        function = "SetEffectALL",
        mode = "Lighting",
        effect = "Single",
        light = level.ToString(),
        speed = "2",
        direction = (string?)null,
        nv_save = (string?)null,
        color = CreateColorPayload(r, g, b),
        MonochromeIndex = "33",
        ManualIndex1 = "1",
        ManualIndex2 = "5",
        ManualIndex3 = "9",
        ManualIndex4 = "13",
        ManualIndex5 = "21",
        ManualIndex6 = "25",
        ManualInterval = "10",
        BreathingIndex = "1"
    };

    private static object CreateColorPayload(byte r, byte g, byte b) => new
    {
        isCircular = false,
        ColorBlocks = 1,
        ColorBuffer = new[]
        {
            new
            {
                ID = 0,
                R = r,
                G = g,
                B = b,
                SolidColorEnd = $"{r:X2}{g:X2}{b:X2}",
                R_position = 0,
                W_position = 100,
                B_position = 100
            }
        }
    };
}
