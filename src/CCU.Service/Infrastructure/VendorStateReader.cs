using System.Text;
using System.Text.Json.Nodes;
using CCU.Shared.Models;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 智控中心状态只读读取器。
/// 从原厂安装目录的配置文件（MainOption.json / Mode3|Mode4_Profile*.json）
/// 和 UWP settings.dat（自定义 Profile 别名）解析当前模式状态。
/// 只读，永不写这些文件。逻辑与 Mode Tray 验证过的实现一致。
/// </summary>
public sealed class VendorStateReader
{
    private const string PackageFamilyName = "CCU.WinUI_wrbgcf7aesyd8";
    private readonly ILogger<VendorStateReader> _logger;
    private IReadOnlyDictionary<int, string> _aliases = new Dictionary<int, string>();
    private string? _installDir;

    public VendorStateReader(ILogger<VendorStateReader> logger, string? installDirOverride = null,
        string? settingsDatPathOverride = null)
    {
        _logger = logger;
        _installDir = installDirOverride;       // 测试注入点；生产为 null 时自动发现
        _settingsDatPath = settingsDatPathOverride;
    }

    private readonly string? _settingsDatPath;

    /// <summary>智控中心配置目录（懒发现，找不到返回 null）。</summary>
    private string? Install
    {
        get
        {
            if (_installDir != null) return _installDir;
            _installDir = Directory.GetDirectories(@"C:\Program Files\OEM")
                .Select(path => Path.Combine(path, "AiStoneService", "MyControlCenter"))
                .FirstOrDefault(Directory.Exists);
            return _installDir;
        }
    }

    /// <summary>MainOption.json 路径；智控中心未安装时为 null。</summary>
    public string? MainOptionPath
    {
        get
        {
            var install = Install;
            return install is null ? null : Path.Combine(install, "UserPofiles", "MainOption.json");
        }
    }

    private string? TurboProfilePath(int index)
    {
        var install = Install;
        return install is null ? null : Path.Combine(install, "UserPofiles", $"Mode3_Profile{index + 1}.json");
    }

    /// <summary>
    /// 读取当前模式状态。智控中心未安装或文件不可读时返回 <see cref="VendorModeState.Unknown"/>。
    /// </summary>
    public VendorModeState ReadModeState()
    {
        try
        {
            var main = MainOptionPath;
            if (main is null || !File.Exists(main)) return VendorModeState.Unknown;

            var json = JsonNode.Parse(File.ReadAllText(main));
            if (json is null) return VendorModeState.Unknown;

            var operatingMode = json["OperatingMode"]?.GetValue<int>() ?? -1;
            var turboIndex = json["TurboProfileIndex"]?.GetValue<int>() ?? 0;
            var turboSilent = -1;
            var turboExtreme = -1;
            if (operatingMode == 2)
            {
                var turboPath = TurboProfilePath(turboIndex);
                if (turboPath is not null && File.Exists(turboPath))
                {
                    var profile = JsonNode.Parse(File.ReadAllText(turboPath));
                    turboSilent = profile?["Silent"]?.GetValue<int>() ?? -1;
                    turboExtreme = profile?["Extreme"]?.GetValue<int>() ?? -1;
                }
            }

            return new VendorModeState(
                operatingMode,
                json["CustomProfileIndex"]?.GetValue<int>() ?? -1,
                turboIndex,
                turboSilent,
                turboExtreme,
                json["TurboGPUOCOffset"]?.GetValue<int>() ?? 0,
                json["FanBoostEnable"]?.GetValue<int>() ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取智控中心状态失败");
            return VendorModeState.Unknown;
        }
    }

    /// <summary>
    /// 发现自定义 Profile 槽位（Mode4_Profile*.json，含 Activated/CustomizeName）。
    /// </summary>
    public IReadOnlyList<CustomProfileInfo> DiscoverCustomProfiles()
    {
        var result = new List<CustomProfileInfo>();
        try
        {
            var install = Install;
            if (install is null) return result;
            var directory = Path.Combine(install, "UserPofiles");
            if (!Directory.Exists(directory)) return result;

            result.AddRange(Directory.EnumerateFiles(directory, "Mode4_Profile*.json")
                .Select(path => (Path: path,
                    Suffix: Path.GetFileNameWithoutExtension(path)["Mode4_Profile".Length..]))
                .Where(p => int.TryParse(p.Suffix, out var n) && n >= 1)
                .OrderBy(p => int.Parse(p.Suffix))
                .Select(p =>
                {
                    var index = int.Parse(p.Suffix) - 1;
                    try
                    {
                        var json = JsonNode.Parse(File.ReadAllText(p.Path));
                        return new CustomProfileInfo(
                            index,
                            json?["Activated"]?.GetValue<bool>() == true,
                            json?["CustomizeName"]?.GetValue<string>()?.Trim());
                    }
                    catch
                    {
                        return new CustomProfileInfo(index, false, null);
                    }
                }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自定义 Profile 发现失败");
        }
        return result;
    }

    /// <summary>
    /// 从 UWP settings.dat 读取自定义 Profile 别名（只读共享打开，不影响智控中心运行）。
    /// </summary>
    public IReadOnlyDictionary<int, string> LoadProfileAliases()
    {
        try
        {
            var path = _settingsDatPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", PackageFamilyName, "Settings", "settings.dat");
            if (!File.Exists(path)) return _aliases;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var hiveText = Encoding.Unicode.GetString(memory.ToArray());

            var aliases = ParseAliasesFromHive(hiveText);
            if (aliases.Count > 0)
            {
                _logger.LogDebug("从智控中心 settings.dat 读取到 {Count} 个自定义 Profile 别名", aliases.Count);
                _aliases = aliases;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "settings.dat 别名读取失败，沿用缓存");
        }
        return _aliases;
    }

    /// <summary>
    /// 从 settings.dat 蜂巢文本中提取自定义 Profile 别名（静态纯函数，便于单测）。
    /// </summary>
    public static IReadOnlyDictionary<int, string> ParseAliasesFromHive(string hiveText)
    {
        var aliases = new Dictionary<int, string>();
        const string customMarker = "\"Mode\": \"Custom\"";
        var markerIndex = hiveText.IndexOf(customMarker, StringComparison.Ordinal);
        if (markerIndex < 0) return aliases;

        var arrayStart = hiveText.LastIndexOf('[', markerIndex);
        var arrayEnd = hiveText.IndexOf(']', markerIndex);
        if (arrayStart < 0 || arrayEnd <= arrayStart) return aliases;

        var profiles = JsonNode.Parse(hiveText[arrayStart..(arrayEnd + 1)])?.AsArray();
        if (profiles is null) return aliases;

        foreach (var profile in profiles)
        {
            if (!string.Equals(profile?["Mode"]?.GetValue<string>(), "Custom",
                    StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(profile?["Index"]?.GetValue<string>(), out var index))
            {
                continue;
            }

            var alias = profile?["AliasName"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(alias))
            {
                aliases[index] = alias;
            }
        }
        return aliases;
    }

    /// <summary>
    /// 生成当前可用的完整模式目录（标准模式 + 已激活的自定义 Profile）。
    /// </summary>
    public IReadOnlyList<VendorModeDefinition> BuildModeCatalog()
    {
        var modes = new List<VendorModeDefinition>(VendorModeDefinition.Defaults.Take(4));
        var aliases = LoadProfileAliases();
        var customDefaults = VendorModeDefinition.Defaults
            .Where(m => m.OperatingMode == 3)
            .ToDictionary(m => m.ProfileIndex);

        foreach (var profile in DiscoverCustomProfiles())
        {
            if (!profile.Activated) continue;
            var label = aliases.TryGetValue(profile.Index, out var alias) && !string.IsNullOrWhiteSpace(alias)
                ? $"Profile {profile.Index + 1} - {alias}"
                : !string.IsNullOrWhiteSpace(profile.CustomizeName) &&
                  !int.TryParse(profile.CustomizeName, out _)
                    ? $"Profile {profile.Index + 1} - {profile.CustomizeName}"
                    : customDefaults.TryGetValue(profile.Index, out var def)
                        ? def.Label
                        : $"Profile {profile.Index + 1}";
            modes.Add(new VendorModeDefinition(label, "OPERATING_CUSTOM_MODE", 3, profile.Index));
        }
        return modes;
    }
}
