using CCU.Shared.Models;
using Xunit;

namespace CCU.Tests;

/// <summary>
/// 模式匹配矩阵 — 覆盖 SetPerformanceMode/自动绑定共用的核心判定逻辑
/// </summary>
public class ModeMatchingTests
{
    [Fact]
    public void Office_Matches_WhenOperatingMode0()
    {
        var office = new VendorModeDefinition("办公", "OPERATING_OFFICE_MODE", 0, 0);
        Assert.True(office.Matches(new VendorModeState(0, -1, 0, -1, -1, 0, 0)));
        Assert.False(office.Matches(new VendorModeState(1, -1, 0, -1, -1, 0, 0)));
    }

    [Fact]
    public void Custom_Matches_OnlySameProfileIndex()
    {
        var p2 = new VendorModeDefinition("Profile 2", "OPERATING_CUSTOM_MODE", 3, 1);
        Assert.True(p2.Matches(new VendorModeState(3, 1, 0, -1, -1, 0, 0)));
        Assert.False(p2.Matches(new VendorModeState(3, 0, 0, -1, -1, 0, 0))); // 同模式不同槽位
        Assert.False(p2.Matches(new VendorModeState(0, 1, 0, -1, -1, 0, 0))); // 同槽位不同模式
    }

    [Fact]
    public void TurboSilent_RequiresSilentFlag()
    {
        var silent = new VendorModeDefinition("狂暴·静技", "OPERATING_TURBO_MODE", 2, 0, Silent: 1, Extreme: 0);
        var extreme = new VendorModeDefinition("狂暴·极速", "OPERATING_TURBO_MODE", 2, 0, Silent: 0, Extreme: 1);

        Assert.True(silent.Matches(new VendorModeState(2, -1, 0, 1, 0, 0, 0)));
        Assert.False(silent.Matches(new VendorModeState(2, -1, 0, 0, 1, 0, 0))); // 极速状态
        Assert.True(extreme.Matches(new VendorModeState(2, -1, 0, 0, 1, 0, 0)));

        // 狂暴细分不匹配其他 TurboProfileIndex
        Assert.False(silent.Matches(new VendorModeState(2, -1, 1, 1, 0, 0, 0)));
    }

    [Fact]
    public void Defaults_ContainFourStandardAndFourCustomSlots()
    {
        Assert.Equal(8, VendorModeDefinition.Defaults.Count);
        Assert.Equal(4, VendorModeDefinition.Defaults.Count(m => m.OperatingMode == 3));
        Assert.Equal([0, 1, 2, 3], VendorModeDefinition.Defaults
            .Where(m => m.OperatingMode == 3).Select(m => m.ProfileIndex));
    }

    [Fact]
    public void TurboProfiles_DifferOnlyInSilentExtreme()
    {
        var silent = VendorModeDefinition.Defaults.First(m => m.Silent == 1);
        var extreme = VendorModeDefinition.Defaults.First(m => m.Extreme == 1);
        Assert.NotEqual(silent, extreme);
        Assert.Equal(silent.Action, extreme.Action); // 同一 Action, 靠后续细分命令区分
    }
}
