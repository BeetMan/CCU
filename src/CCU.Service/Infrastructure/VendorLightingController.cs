using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 原厂灯光控制 — 经 GCUBridge MQTT。
///
/// 协议来源:
/// - 静态色命令: DynamicLightingBridge 实测验证 (SetEffectALL / effect=Single)
/// - 灯效词表: 原厂 CCUWinUI 反编译源码 RGBKB_Effect 枚举 (26 种)
/// - 方向和色盘: 原厂 RgbKeyboardView::SetDirection/ChangeEffectColorCount/SendToServer
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

    public static readonly IReadOnlyList<string> SupportedWaveDirections =
        ["LeftRight", "RightLeft"];

    /// <summary>设置键盘灯效。</summary>
    public void ApplyKeyboard(
        string effect, byte r, byte g, byte b, int brightnessLevel, int speed, bool on,
        string? direction = null)
    {
        if (!on || brightnessLevel <= 0)
        {
            _mqtt.PublishTopic(KeyboardTopic, new { function = "SetPower", powerstatus = 0 });
            _logger.LogInformation("键盘灯已关闭");
            return;
        }

        _mqtt.PublishTopic(KeyboardTopic, new { function = "SetPower", powerstatus = 1 });

        var effectiveDirection = ResolveDirection(effect, direction);
        _mqtt.PublishTopic(KeyboardTopic,
            CreateEffectCommand(effect, r, g, b, brightnessLevel, speed, effectiveDirection));
        _logger.LogInformation(
            "键盘灯效: {Effect} RGB({R},{G},{B}) 亮度{Level} 速度{Speed} 方向{Direction}",
            effect, r, g, b, brightnessLevel, speed, effectiveDirection);
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

    /// <summary>效果命令 — 与原厂 SendToServer 结构一致。</summary>
    private static object CreateEffectCommand(
        string effect, byte r, byte g, byte b, int level, int speed, string direction) => new
    {
        function = "SetEffectALL",
        mode = "Lighting",
        effect,
        light = level.ToString(),
        speed = Math.Clamp(speed, 1, 5).ToString(),
        direction,
        nv_save = "0",
        color = CreateColorPayload(r, g, b, UsesRainbowPalette(effect)),
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
        direction = "None",
        nv_save = "0",
        color = CreateColorPayload(r, g, b, useRainbowPalette: false),
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

    private static string ResolveDirection(string effect, string? requestedDirection)
    {
        if (!effect.Equals("Wave", StringComparison.OrdinalIgnoreCase) &&
            !effect.Equals("Sine", StringComparison.OrdinalIgnoreCase) &&
            !effect.Equals("Diagonal", StringComparison.OrdinalIgnoreCase))
            return "None";

        if (string.IsNullOrWhiteSpace(requestedDirection))
            return "LeftRight";

        var direction = SupportedWaveDirections.FirstOrDefault(
            candidate => candidate.Equals(requestedDirection, StringComparison.OrdinalIgnoreCase));
        return direction ?? throw new ArgumentException($"不支持的波浪方向: {requestedDirection}");
    }

    private static bool UsesRainbowPalette(string effect) =>
        effect.Equals("Wave", StringComparison.OrdinalIgnoreCase) ||
        effect.Equals("ColorfulWave", StringComparison.OrdinalIgnoreCase) ||
        effect.Equals("Rainbow", StringComparison.OrdinalIgnoreCase);

    private static object CreateColorPayload(byte r, byte g, byte b, bool useRainbowPalette)
    {
        (byte R, byte G, byte B)[] colors = useRainbowPalette
            ?
            [
                (255, 0, 0), (255, 165, 0), (255, 255, 0), (0, 255, 0),
                (0, 0, 255), (0, 255, 255), (139, 0, 255)
            ]
            : Enumerable.Repeat((r, g, b), 7).ToArray();

        return new
        {
            isCircular = true,
            ColorBlocks = colors.Length,
            ColorBuffer = colors.Select((color, index) => new
            {
                ID = index,
                color.R,
                color.G,
                color.B,
                SolidColorEnd = $"{color.R:X2}{color.G:X2}{color.B:X2}",
                R_position = 0,
                W_position = 100,
                B_position = 100
            }).ToArray()
        };
    }
}
