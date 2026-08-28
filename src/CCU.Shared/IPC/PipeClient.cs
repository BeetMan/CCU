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
    public async Task<IpcMessage?> SendAsync(IpcMessage message)
    {
        if (_stream == null || !_stream.IsConnected)
            return null;

        try
        {
            var json = JsonSerializer.Serialize(message);
            byte[] msgBytes = Encoding.UTF8.GetBytes(json);
            byte[] lenPrefix = BitConverter.GetBytes(msgBytes.Length);

            // 写: 4 字节长度 + 消息体
            await _stream.WriteAsync(lenPrefix, 0, 4);
            await _stream.WriteAsync(msgBytes, 0, msgBytes.Length);
            await _stream.FlushAsync();

            // 读响应长度
            byte[] respLen = new byte[4];
            if (await _stream.ReadAsync(respLen, 0, 4) != 4) return null;
            int len = BitConverter.ToInt32(respLen, 0);
            if (len <= 0 || len > 65536) return null;

            // 读响应体
            byte[] respBuf = new byte[len];
            int total = 0;
            while (total < len)
            {
                int n = await _stream.ReadAsync(respBuf, total, len - total);
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

    public void Disconnect() => _stream?.Close();
    public void Dispose() => _stream?.Dispose();
}
