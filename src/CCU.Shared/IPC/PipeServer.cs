using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CCU.Shared.IPC;

/// <summary>
/// Named Pipe 服务端 — 基于 byte[] 帧的可靠通信
/// </summary>
public class PipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<IpcMessage, Task<IpcMessage>> _messageHandler;
    private readonly Thread _listenThread;

    public PipeServer(string pipeName, Func<IpcMessage, Task<IpcMessage>> messageHandler)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "PipeServer" };
    }

    public void Start() => _listenThread.Start();

    private void ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                // 允许所有经过身份验证的用户连接 (解决 SYSTEM 服务 pipe 的 "Access denied" 问题)
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null),
                    PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));

                using var server = NamedPipeServerStreamAcl.Create(
                    _pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    inBufferSize: 65536, outBufferSize: 65536,
                    pipeSecurity);

                server.WaitForConnection();
                HandleConnection(server);
                server.Disconnect();
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { /* client disconnect, continue */ }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                    System.Diagnostics.Debug.WriteLine($"PipeServer error: {ex.Message}");
            }
        }
    }

    private void HandleConnection(NamedPipeServerStream stream)
    {
        try
        {
            // 读消息 — 先读 4 字节长度前缀
            byte[] lenBuf = new byte[4];
            if (stream.Read(lenBuf, 0, 4) != 4) return;
            int msgLen = BitConverter.ToInt32(lenBuf, 0);
            if (msgLen <= 0 || msgLen > 65536) return;

            byte[] msgBuf = new byte[msgLen];
            int totalRead = 0;
            while (totalRead < msgLen)
            {
                int n = stream.Read(msgBuf, totalRead, msgLen - totalRead);
                if (n <= 0) return;
                totalRead += n;
            }

            string json = Encoding.UTF8.GetString(msgBuf, 0, totalRead);
            var message = JsonSerializer.Deserialize<IpcMessage>(json);
            if (message == null) return;

            // 处理
            var response = _messageHandler(message).GetAwaiter().GetResult();
            var respJson = JsonSerializer.Serialize(response);
            byte[] respBytes = Encoding.UTF8.GetBytes(respJson);

            // 写响应 — 长度前缀
            byte[] respLen = BitConverter.GetBytes(respBytes.Length);
            stream.Write(respLen, 0, 4);
            stream.Write(respBytes, 0, respBytes.Length);
            stream.Flush();
        }
        catch { /* connection lost */ }
    }

    public void Stop() => _cts.Cancel();
    public void Dispose() => _cts.Dispose();
}
