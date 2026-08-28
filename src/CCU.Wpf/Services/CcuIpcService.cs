using System.Text.Json;
using System.Text.Json.Serialization;
using CCU.Shared.IPC;

namespace CCU.Wpf.Services;

/// <summary>
/// WPF 端的 IPC 客户端封装 — 维护与服务的长连接
/// </summary>
public class CcuIpcService : IDisposable
{
    private PipeClient? _client;
    private readonly string _pipeName = "CCU.Service.Pipe";
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<bool> ConnectAsync()
    {
        _client = new PipeClient(_pipeName);
        return await _client.ConnectAsync();
    }

    public async Task<T?> SendAsync<T>(IpcMessageType type, object payload) where T : class
    {
        if (_client == null || !_client.IsConnected)
        {
            var reconnected = await ConnectAsync();
            if (!reconnected) return null;
        }

        try
        {
            var msg = IpcMessage.Create(type, payload);
            var response = await _client!.SendAsync(msg);
            if (response == null || response.Type == IpcMessageType.Error) return null;
            return response.DeserializePayload<T>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SendCommandAsync(IpcMessageType type, object payload)
    {
        if (_client == null || !_client.IsConnected)
        {
            var reconnected = await ConnectAsync();
            if (!reconnected) return false;
        }

        try
        {
            var msg = IpcMessage.Create(type, payload);
            var response = await _client!.SendAsync(msg);
            return response != null && response.Type == IpcMessageType.Ack;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
    }
}

/// <summary>
/// 硬件信息 DTO (与服务端 HardwareInfo 对应)
/// </summary>
public class HardwareInfoDto
{
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
    public double CpuUsage { get; set; }
    public double GpuUsage { get; set; }
    public double CpuFanSpeed { get; set; }
    public double GpuFanSpeed { get; set; }
    public double CpuPower { get; set; }
    public double GpuPower { get; set; }
    public double CpuFrequency { get; set; }
    public double GpuCoreFrequency { get; set; }
    public double GpuMemFrequency { get; set; }
    public double BatteryLevel { get; set; }
    public double MemoryUsage { get; set; }

    // 模式状态 (MQTT-first 服务提供)
    public int OperatingMode { get; set; } = -1;
    public int CustomProfileIndex { get; set; } = -1;
    public int TurboGpuOcOffset { get; set; }
    public int FanBoostEnabled { get; set; }
    public string ModeLabel { get; set; } = "";

    public string? Timestamp { get; set; }
}
