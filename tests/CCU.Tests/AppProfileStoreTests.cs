using CCU.Service.Core;
using CCU.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCU.Tests;

/// <summary>
/// 应用绑定存储 — 使用注入的临时路径，绝不触碰真实 ProgramData
/// </summary>
public class AppProfileStoreTests : IDisposable
{
    private readonly string _storePath;
    private readonly AppProfileStore _store;

    public AppProfileStoreTests()
    {
        _storePath = Path.Combine(Path.GetTempPath(), $"ccu-test-{Guid.NewGuid():N}", "app-profiles.json");
        _store = new AppProfileStore(NullLogger<AppProfileStore>.Instance, _storePath);
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        var dir = Path.GetDirectoryName(_storePath);
        if (dir != null && Directory.Exists(dir)) Directory.Delete(dir);
    }

    [Fact]
    public void SaveProfile_Persists_ToDisk()
    {
        _store.SaveProfile(new AppProfile { Process = "game.exe", Mode = 2, Silent = 0, Extreme = 1 });

        Assert.True(File.Exists(_storePath));
        // 新实例从磁盘恢复（模拟服务重启）
        var reloaded = new AppProfileStore(NullLogger<AppProfileStore>.Instance, _storePath);
        Assert.NotNull(reloaded.FindBinding("GAME.EXE")); // 大小写不敏感
        Assert.Equal(2, reloaded.FindBinding("game.exe")!.Mode);
    }

    [Fact]
    public void SaveProfile_Dedupes_ByProcess()
    {
        _store.SaveProfile(new AppProfile { Process = "game.exe", Mode = 1 });
        _store.SaveProfile(new AppProfile { Process = "Game.exe", Mode = 2, Silent = 0, Extreme = 1 });

        var settings = _store.Current;
        Assert.Single(settings.Profiles);           // 同进程去重（大小写不敏感）
        Assert.Equal(2, settings.Profiles[0].Mode); // 保留最后一次
    }

    [Fact]
    public void DeleteProfile_Removes_And_Persists()
    {
        _store.SaveProfile(new AppProfile { Process = "a.exe", Mode = 0 });
        _store.SaveProfile(new AppProfile { Process = "b.exe", Mode = 1 });

        Assert.True(_store.DeleteProfile("a.exe"));
        Assert.False(_store.DeleteProfile("a.exe")); // 二次删除返回 false

        var reloaded = new AppProfileStore(NullLogger<AppProfileStore>.Instance, _storePath);
        Assert.Null(reloaded.FindBinding("a.exe"));
        Assert.NotNull(reloaded.FindBinding("b.exe"));
    }

    [Fact]
    public void SetEnabled_Persists()
    {
        _store.SetEnabled(true);
        var reloaded = new AppProfileStore(NullLogger<AppProfileStore>.Instance, _storePath);
        Assert.True(reloaded.Current.Enabled);
        Assert.False(reloaded.Current.Profiles.Any()); // 开关与规则独立
    }

    [Fact]
    public void FindBinding_NullInput_ReturnsNull()
    {
        _store.SaveProfile(new AppProfile { Process = "a.exe", Mode = 0 });
        Assert.Null(_store.FindBinding(null));
        Assert.Null(_store.FindBinding(""));
        Assert.Null(_store.FindBinding("unknown.exe"));
    }
}
