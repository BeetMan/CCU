using System.Text;
using CCU.Service.Infrastructure;
using CCU.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCU.Tests;

/// <summary>
/// 智控中心状态读取器 — 用 fixture 配置目录测试，不依赖真机
/// </summary>
public class VendorStateReaderTests : IDisposable
{
    private readonly string _fixtureDir;
    private readonly string _settingsDat;

    public VendorStateReaderTests()
    {
        _fixtureDir = Path.Combine(Path.GetTempPath(), $"ccu-fix-{Guid.NewGuid():N}",
            "AiStoneService", "MyControlCenter", "UserPofiles");
        Directory.CreateDirectory(_fixtureDir);
        _settingsDat = Path.Combine(Path.GetTempPath(), $"ccu-fix-{Guid.NewGuid():N}", "settings.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsDat)!);
        // 空 settings.dat — 隔离真机数据, 别名测试单独写入
        File.WriteAllBytes(_settingsDat, Array.Empty<byte>());
    }

    public void Dispose()
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(_fixtureDir, "..", ".."));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var sdir = Path.GetDirectoryName(_settingsDat);
            if (sdir != null && Directory.Exists(sdir)) Directory.Delete(sdir, recursive: true);
        }
        catch (IOException) { /* 临时目录清理失败不影响测试结果 */ }
    }

    private VendorStateReader CreateReader() =>
        new(NullLogger<VendorStateReader>.Instance, Path.GetDirectoryName(_fixtureDir), _settingsDat);

    [Fact]
    public void ReadModeState_Parses_MainOption()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, "MainOption.json"),
            """{"OperatingMode": 2, "CustomProfileIndex": 1, "TurboProfileIndex": 0, "TurboGPUOCOffset": 150, "FanBoostEnable": 1}""");
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode3_Profile1.json"),
            """{"Silent": 0, "Extreme": 1}""");

        var state = CreateReader().ReadModeState();

        Assert.Equal(2, state.OperatingMode);
        Assert.Equal(1, state.CustomProfileIndex);
        Assert.Equal(0, state.TurboProfileIndex);
        Assert.Equal(0, state.TurboSilent);
        Assert.Equal(1, state.TurboExtreme);
        Assert.Equal(150, state.TurboGpuOcOffset);
        Assert.Equal(1, state.FanBoostEnabled);
    }

    [Fact]
    public void ReadModeState_TurboReadsProfileFile()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, "MainOption.json"),
            """{"OperatingMode": 2, "TurboProfileIndex": 0}""");
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode3_Profile1.json"),
            """{"Silent": 1, "Extreme": 0}""");

        Assert.Equal(1, CreateReader().ReadModeState().TurboSilent);
    }

    [Fact]
    public void ReadModeState_MissingFiles_ReturnsUnknown()
    {
        var state = CreateReader().ReadModeState();
        Assert.Equal(-1, state.OperatingMode);
        Assert.Equal(VendorModeState.Unknown, state);
    }

    [Fact]
    public void ReadModeState_CorruptedJson_ReturnsUnknown_NoThrow()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, "MainOption.json"), "{ broken json !!");
        Assert.Equal(-1, CreateReader().ReadModeState().OperatingMode);
    }

    [Fact]
    public void BuildModeCatalog_IncludesActiveCustomProfiles()
    {
        File.WriteAllText(Path.Combine(_fixtureDir, "MainOption.json"), """{"OperatingMode": 0}""");
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode4_Profile1.json"),
            """{"Activated": true, "CustomizeName": "静音高性能"}""");
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode4_Profile2.json"),
            """{"Activated": false, "CustomizeName": "未启用"}""");
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode4_Profile4.json"),
            """{"Activated": true, "CustomizeName": ""}""");

        var catalog = CreateReader().BuildModeCatalog();

        var customs = catalog.Where(m => m.OperatingMode == 3).ToList();
        Assert.Equal(2, customs.Count); // 未激活的 Profile 2 不出现
        Assert.Equal(0, customs[0].ProfileIndex);
        Assert.Equal("Profile 1 - 静音高性能", customs[0].Label); // CustomizeName 作为显示名
        Assert.Equal(3, customs[1].ProfileIndex);
        Assert.Equal("Profile 4", customs[1].Label);             // 空名回退默认
    }

    [Fact]
    public void BuildModeCatalog_WithoutVendor_ReturnsDefaults()
    {
        var reader = new VendorStateReader(NullLogger<VendorStateReader>.Instance,
            Path.Combine(Path.GetTempPath(), $"ccu-nonexist-{Guid.NewGuid():N}"));
        var catalog = reader.BuildModeCatalog();
        Assert.Equal(4, catalog.Count); // 仅标准模式
        Assert.All(catalog, m => Assert.NotEqual(3, m.OperatingMode));
    }

    // ========================
    // settings.dat 蜂巢别名解析
    // ========================

    [Fact]
    public void LoadProfileAliases_Reads_HiveFile()
    {
        // 模拟 settings.dat: UTF-16 蜂巢文本 (真机即此格式)
        var hive = "junk_header [{\"Mode\": \"Custom\", \"Index\": \"0\", \"AliasName\": \"静音均衡\"}] junk_footer";
        File.WriteAllBytes(_settingsDat, Encoding.Unicode.GetBytes(hive));

        var catalog = CreateReader().BuildModeCatalog();
        File.WriteAllText(Path.Combine(_fixtureDir, "MainOption.json"), "{}" );
        File.WriteAllText(Path.Combine(_fixtureDir, "Mode4_Profile1.json"),
            """{"Activated": true, "CustomizeName": "静音高性能"}""");

        catalog = CreateReader().BuildModeCatalog();
        var p1 = catalog.First(m => m.OperatingMode == 3 && m.ProfileIndex == 0);
        Assert.Equal("Profile 1 - 静音均衡", p1.Label); // 别名优先于 CustomizeName (与 Mode Tray 行为一致)
    }

    [Fact]
    public void ParseAliasesFromHive_Extracts_CustomAliases()
    {
        // 模拟 settings.dat 中的 JSON 片段（真机蜂巢是 UTF-16 文本，含多段 JSON）
        var hive = """
            garbage_before [{"Mode": "Custom", "Index": "0", "AliasName": "静音高性能"},
            {"Mode": "Custom", "Index": "2", "AliasName": "  静音游戏  "},
            {"Mode": "Office", "Index": "9", "AliasName": "忽略"}] garbage_after
            """;

        var aliases = VendorStateReader.ParseAliasesFromHive(hive);

        Assert.Equal(2, aliases.Count);
        Assert.Equal("静音高性能", aliases[0]);
        Assert.Equal("静音游戏", aliases[2]); // Trim 生效
    }

    [Fact]
    public void ParseAliasesFromHive_NoMarker_ReturnsEmpty()
    {
        Assert.Empty(VendorStateReader.ParseAliasesFromHive("nothing here"));
        Assert.Empty(VendorStateReader.ParseAliasesFromHive(""));
    }
}
