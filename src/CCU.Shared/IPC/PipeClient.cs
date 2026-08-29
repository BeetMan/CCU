using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CCU.Shared.IPC;

/// <summary>
/// Named Pipe 客户端 — 基于 byte[] 帧 (长度前缀)
/// </summary>
public class PipeClient : IDisposable
{
    private readonly string _pipeName;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private NamedPipeClientStream? _stream;

    public bool IsConnected => _stream?.IsConnected == true;

    public PipeClient(string pipeName) => _pipeName = pipeName;

    public async Task<bool> ConnectAsync(int timeoutMs = 5000)
    {
        _stream?.Dispose();
        _stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await _stream.ConnectAsync(timeoutMs);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    /// <summary>
    /// 发送消息并等待响应
    /// </summary>
    public async Task<IpcMessage?> SendAsync(IpcMessage message) =>
        await SendAsync(message, TimeSpan.FromSeconds(20));

    public async Task<IpcMessage?> SendAsync(IpcMessage message, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        await _sendGate.WaitAsync(timeoutCts.Token);
        try
        {
            if (_stream == null || !_stream.IsConnected)
                return null;

            try
            {
                var json = JsonSerializer.Serialize(message);
                byte[] msgBytes = Encoding.UTF8.GetBytes(json);
                byte[] lenPrefix = BitConverter.GetBytes(msgBytes.Length);

                // 写: 4 字节长度 + 消息体
                await _stream.WriteAsync(lenPrefix, timeoutCts.Token);
                await _stream.WriteAsync(msgBytes, timeoutCts.Token);
                await _stream.FlushAsync(timeoutCts.Token);

                // 读响应长度
                byte[] respLen = new byte[4];
                if (await _stream.ReadAsync(respLen, timeoutCts.Token) != 4) return null;
                int len = BitConverter.ToInt32(respLen, 0);
                if (len <= 0 || len > 65536) return null;

                // 读响应体
                byte[] respBuf = new byte[len];
                int total = 0;
                while (total < len)
                {
                    int n = await _stream.ReadAsync(
                        respBuf.AsMemory(total, len - total), timeoutCts.Token);
                    if (n <= 0) return null;
                    total += n;
                }

                string respJson = Encoding.UTF8.GetString(respBuf, 0, total);
                return JsonSerializer.Deserialize<IpcMessage>(respJson);
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Disconnect() => _stream?.Close();
    public void Dispose() => _stream?.Dispose();

    /// <summary>测试辅助：绕过类型化协议直接写原始帧（坏消息/边界测试用）。</summary>
    public async Task TestWriteRawAsync(byte[] lenPrefix, byte[] body)
    {
        if (_stream == null || !_stream.IsConnected)
            throw new InvalidOperationException("pipe not connected");
        await _stream.WriteAsync(lenPrefix, 0, lenPrefix.Length);
        await _stream.WriteAsync(body, 0, body.Length);
        await _stream.FlushAsync();
    }
}
