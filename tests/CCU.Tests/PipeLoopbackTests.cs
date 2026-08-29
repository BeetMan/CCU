using System.Text;
using System.Text.Json;
using CCU.Shared.IPC;
using Xunit;

namespace CCU.Tests;

/// <summary>
/// Pipe 帧协议回环测试 — 核心回归点：
/// v21 曾因服务端写完响应立即 Disconnect 导致小响应被内核丢弃（bug B1）。
/// 修复后小/大响应都必须完整到达。
/// </summary>
public class PipeLoopbackTests
{
    private static Task<IpcMessage> Handler(IpcMessage request)
    {
        // 模拟服务端: 按请求返回不同大小响应
        IpcMessage response = request.Type switch
        {
            IpcMessageType.GetHardwareInfo => IpcMessage.Create(
                IpcMessageType.HardwareInfoUpdate, new { CpuTemperature = 45.5, GpuTemperature = 43.0 }),
            IpcMessageType.EcDiagnostic => IpcMessage.Create(
                IpcMessageType.Ack, new { Success = true, Report = new string('X', 12000) }),
            IpcMessageType.SetPerformanceMode => IpcMessage.Create(
                IpcMessageType.Ack, new { Success = true, Mode = "办公模式" }),
            _ => IpcMessage.Create(IpcMessageType.Error, new { Message = "unknown" })
        };
        return Task.FromResult(response);
    }

    private static async Task<IpcMessage?> RoundtripAsync(string pipeName, IpcMessage request)
    {
        using var server = new PipeServer(pipeName, Handler);
        server.Start();

        using var client = new PipeClient(pipeName);
        if (!await client.ConnectAsync(3000)) throw new TimeoutException("connect failed");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task SmallResponse_Arrives_Intact()
    {
        var resp = await RoundtripAsync("CCU.Test.Pipe.Small", IpcMessage.Create(IpcMessageType.SetPerformanceMode, new { Mode = 0 }));
        Assert.NotNull(resp);
        Assert.Equal(IpcMessageType.Ack, resp!.Type);
        // 走真实客户端反序列化路径 (JSON 里非 ASCII 会被转义, 不能直接字符串匹配)
        var payload = resp.DeserializePayload<ModeAckPayload>();
        Assert.True(payload!.Success);
        Assert.Equal("办公模式", payload.Mode);
    }

    private sealed record ModeAckPayload(bool Success, string Mode);

    [Fact]
    public async Task HardwareInfoResponse_Arrives_Intact()
    {
        var resp = await RoundtripAsync("CCU.Test.Pipe.Info", IpcMessage.Create(IpcMessageType.GetHardwareInfo, new { }));
        Assert.NotNull(resp);
        Assert.Equal(IpcMessageType.HardwareInfoUpdate, resp!.Type);
        Assert.Contains("45.5", resp.Payload);
    }

    [Fact]
    public async Task LargeResponse_Arrives_Intact()
    {
        var resp = await RoundtripAsync("CCU.Test.Pipe.Large", IpcMessage.Create(IpcMessageType.EcDiagnostic, new { }));
        Assert.NotNull(resp);
        Assert.Contains("XXXX", resp.Payload);
    }

    [Fact]
    public async Task MalformedJson_GracefullyClosed_NoCrash()
    {
        var pipeName = "CCU.Test.Pipe.Bad";
        using var server = new PipeServer(pipeName, Handler, log: _ => { });
        server.Start();

        using var client = new PipeClient(pipeName);
        Assert.True(await client.ConnectAsync(3000));

        // 直接写一段坏 JSON 帧
        var bad = Encoding.UTF8.GetBytes("{ this is not json");
        var len = BitConverter.GetBytes(bad.Length);
        await client.TestWriteRawAsync(len, bad);

        // 服务端应优雅关连接, 且下一个客户端仍能正常服务
        var next = await RoundtripAsync(pipeName, IpcMessage.Create(IpcMessageType.SetPerformanceMode, new { Mode = 1 }));
        Assert.NotNull(next);
        Assert.Equal(IpcMessageType.Ack, next!.Type);
    }

    [Fact]
    public async Task HandlerException_ReturnsErrorMessage()
    {
        var pipeName = "CCU.Test.Pipe.Throw";
        using var server = new PipeServer(pipeName, _ =>
            throw new InvalidOperationException("boom"), log: _ => { });
        server.Start();

        using var client = new PipeClient(pipeName);
        Assert.True(await client.ConnectAsync(3000));
        var resp = await client.SendAsync(IpcMessage.Create(IpcMessageType.GetHardwareInfo, new { }));

        Assert.NotNull(resp);
        Assert.Equal(IpcMessageType.Error, resp!.Type);
        Assert.Contains("boom", resp.Payload);
    }
}
