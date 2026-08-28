namespace CCU.Shared.Models;

/// <summary>
/// 智控中心（GCUBridge MQTT）运行状态 — 从原厂配置文件只读解析。
/// 字段语义与 Mode Tray 对智控中心 5.60.60.17 的观察结果一致。
/// </summary>
public readonly record struct VendorModeState(
    int OperatingMode,
    int CustomProfileIndex,
    int TurboProfileIndex,
    int TurboSilent,
    int TurboExtreme,
    int TurboGpuOcOffset,
    int FanBoostEnabled)
{
    public static readonly VendorModeState Unknown = new(-1, -1, 0, -1, -1, 0, 0);
}

/// <summary>
/// 一个可切换的模式条目（标准模式或自定义 Profile 槽位）。
/// </summary>
public sealed record VendorModeDefinition(
    string Label,
    string Action,
    int OperatingMode,
    int ProfileIndex,
    int? Silent = null,
    int? Extreme = null)
{
    /// <summary>
    /// 与原厂内置一致的标准模式默认目录。
    /// Action 名称来自原厂 GCUBridge 已验证的命令集。
    /// </summary>
    public static readonly IReadOnlyList<VendorModeDefinition> Defaults =
    [
        new("办公模式", "OPERATING_OFFICE_MODE", 0, 0),
        new("游戏模式", "OPERATING_GAMING_MODE", 1, 0),
        new("狂暴 · 静技", "OPERATING_TURBO_MODE", 2, 0, Silent: 1, Extreme: 0),
        new("狂暴 · 极速", "OPERATING_TURBO_MODE", 2, 0, Silent: 0, Extreme: 1),
        new("Profile 1", "OPERATING_CUSTOM_MODE", 3, 0),
        new("Profile 2", "OPERATING_CUSTOM_MODE", 3, 1),
        new("Profile 3", "OPERATING_CUSTOM_MODE", 3, 2),
        new("Profile 4", "OPERATING_CUSTOM_MODE", 3, 3)
    ];

    public bool Matches(VendorModeState state) =>
        state.OperatingMode == OperatingMode &&
        (OperatingMode != 3 || state.CustomProfileIndex == ProfileIndex) &&
        (OperatingMode != 2 ||
            (state.TurboProfileIndex == ProfileIndex &&
             (Silent is null || state.TurboSilent == Silent) &&
             (Extreme is null || state.TurboExtreme == Extreme)));
}

/// <summary>
/// 自定义 Profile 槽位的只读发现结果。
/// </summary>
public sealed record CustomProfileInfo(int Index, bool Activated, string? CustomizeName);
