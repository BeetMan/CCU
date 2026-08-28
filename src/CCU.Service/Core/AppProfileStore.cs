using System.Text.Json;
using CCU.Shared.Models;

namespace CCU.Service.Core;

/// <summary>
/// 应用绑定规则存储 — ProgramData\CCU_Alternative\app-profiles.json
/// </summary>
public sealed class AppProfileStore
{
    private static readonly string StorePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CCU_Alternative", "app-profiles.json");

    private readonly object _sync = new();
    private AppBindingSettings _settings = new();
    private readonly ILogger<AppProfileStore> _logger;

    public AppProfileStore(ILogger<AppProfileStore> logger)
    {
        _logger = logger;
        Load();
    }

    public AppBindingSettings Current
    {
        get { lock (_sync) return _settings; }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _settings.Enabled = enabled;
        }
        Save();
        _logger.LogInformation("应用绑定自动切换已{State}", enabled ? "开启" : "关闭");
    }

    public void SaveProfile(AppProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Process))
            throw new ArgumentException("进程名不能为空");

        lock (_sync)
        {
            _settings.Profiles.RemoveAll(p => p.Matches(profile.Process));
            _settings.Profiles.Add(profile);
        }
        Save();
        _logger.LogInformation("应用绑定已保存: {Process} → mode {Mode}", profile.Process, profile.Mode);
    }

    public bool DeleteProfile(string process)
    {
        bool removed;
        lock (_sync)
        {
            removed = _settings.Profiles.RemoveAll(p => p.Matches(process)) > 0;
        }
        if (removed) Save();
        return removed;
    }

    /// <summary>查询前台进程的绑定（未命中返回 null）。</summary>
    public AppProfile? FindBinding(string? foregroundProcess)
    {
        if (foregroundProcess is null) return null;
        lock (_sync)
        {
            return _settings.Profiles.FirstOrDefault(p => p.Matches(foregroundProcess));
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var loaded = JsonSerializer.Deserialize<AppBindingSettings>(File.ReadAllText(StorePath));
            if (loaded is not null)
            {
                lock (_sync) _settings = loaded;
                _logger.LogInformation("已加载 {Count} 条应用绑定规则 (Enabled={Enabled})",
                    loaded.Profiles.Count, loaded.Enabled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用绑定配置加载失败，使用默认设置");
        }
    }

    private void Save()
    {
        try
        {
            AppBindingSettings snapshot;
            lock (_sync) snapshot = _settings;
            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用绑定配置保存失败");
        }
    }
}
