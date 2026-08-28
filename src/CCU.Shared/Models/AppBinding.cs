namespace CCU.Shared.Models;

/// <summary>
/// 应用绑定规则：前台进程匹配时自动切换到指定模式。
/// </summary>
public sealed class AppProfile
{
    /// <summary>进程名（小写，含 .exe，如 "cyberpunk2077.exe"）</summary>
    public string Process { get; set; } = "";

    /// <summary>目标模式：0=办公 1=游戏 2=狂暴 3=自定义</summary>
    public int Mode { get; set; }

    /// <summary>自定义 Profile 槽位（Mode=3 时有效，0 起）</summary>
    public int? ProfileIndex { get; set; }

    /// <summary>狂暴细分：1=静技 0=极速（Mode=2 时有效）</summary>
    public int? Silent { get; set; }

    /// <summary>狂暴细分：1=极速（Mode=2 时有效）</summary>
    public int? Extreme { get; set; }

    /// <summary>显示名称（可选，默认取进程名）</summary>
    public string Label { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Process : Label;

    public bool Matches(string processName) =>
        !string.IsNullOrWhiteSpace(processName) &&
        string.Equals(processName, Process, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 应用绑定功能的持久化设置（存 ProgramData\CCU_Alternative\app-profiles.json）。
/// </summary>
public sealed class AppBindingSettings
{
    public bool Enabled { get; set; }

    /// <summary>离开绑定应用后是否自动恢复到日常模式（0=办公）</summary>
    public bool RestoreOnLeave { get; set; } = true;

    public List<AppProfile> Profiles { get; set; } = [];
}
