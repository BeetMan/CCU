using System.Collections.Concurrent;
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
    private readonly Action<string>? _log;
    private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _connections = new();

    public PipeServer(string pipeName, Func<IpcMessage, Task<IpcMessage>> messageHandler, Action<string>? log = null)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
        _log = log;
        _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "PipeServer" };
    }

    public void Start() => _listenThread.Start();

    private void ListenLoop()
    {
        try
        {
            // WaitForConnection 在目标运行时会同步等待，因此用独立 acceptor 线程预创建实例。
            // 四个实例足够 WPF、CLI 和诊断工具同时保持连接。
            var acceptors = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(
                AcceptLoop, CancellationToken.None, TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();
            Task.WaitAll(acceptors);
        }
        catch (AggregateException) when (_cts.IsCancellationRequested)
        {
            // Stop() 已关闭所有等待/活动实例。
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
                _log?.Invoke($"PipeServer error: {ex.Message}");
        }
    }

    private void AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            var pipeSecurity = new PipeSecurity();
            // 允许所有经过身份验证的用户连接 (解决 SYSTEM 服务 pipe 的 "Access denied" 问题)
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));
            var serviceIdentity = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (serviceIdentity is not null)
            {
                // 创建后续 Pipe 实例需要 CreateNewInstance；仅授予当前服务身份完全控制。
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    serviceIdentity, PipeAccessRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
            }

            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    _pipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    inBufferSize: 65536, outBufferSize: 65536,
                    pipeSecurity);
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                    _log?.Invoke($"PipeServer acceptor create failed: {ex.Message}");
                return;
            }

            using (server)
            {
                _connections.TryAdd(server, 0);

                try
                {
                    server.WaitForConnection();
                    HandleConnection(server);
                }
                catch (IOException)
                {
                    // 客户端断开；重新创建本 acceptor 的服务实例。
                }
                catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!_cts.IsCancellationRequested)
                        _log?.Invoke($"PipeServer client error: {ex.Message}");
                }
                finally
                {
                    _connections.TryRemove(server, out _);
                }
            }
        }
    }

    private void HandleConnection(NamedPipeServerStream stream)
    {
        try
        {
            // 一个 WPF 客户端会持续轮询并发送控制命令，因此连接必须支持多次请求/响应。
            // 每次响应仍等待客户端读走，避免小响应在连接最终关闭时被内核丢弃。
            while (!_cts.IsCancellationRequested && stream.IsConnected)
            {
                byte[] lenBuf = new byte[4];
                if (!ReadExactly(stream, lenBuf, lenBuf.Length)) return;
                int msgLen = BitConverter.ToInt32(lenBuf, 0);
                if (msgLen <= 0 || msgLen > 65536) return;

                byte[] msgBuf = new byte[msgLen];
                if (!ReadExactly(stream, msgBuf, msgLen)) return;

                string json = Encoding.UTF8.GetString(msgBuf);
                IpcMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<IpcMessage>(json);
                }
                catch (JsonException ex)
                {
                    _log?.Invoke($"PipeServer: bad message json ({ex.Message}), msgLen={msgLen}");
                    return;
                }
                if (message == null) return;

                IpcMessage response;
                try
                {
                    response = _messageHandler(message).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"PipeServer: handler threw for {message.Type}: {ex}");
                    response = IpcMessage.Create(IpcMessageType.Error, new { Message = ex.Message });
                }

                var respJson = JsonSerializer.Serialize(response);
                byte[] respBytes = Encoding.UTF8.GetBytes(respJson);

                try
                {
                    byte[] respLen = BitConverter.GetBytes(respBytes.Length);
                    stream.Write(respLen, 0, respLen.Length);
                    stream.Write(respBytes, 0, respBytes.Length);
                    stream.Flush();
                    stream.WaitForPipeDrain();
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"PipeServer: write/drain failed: {ex.Message}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"PipeServer: connection aborted: {ex.Message}");
        }
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, totalRead, count - totalRead);
            if (read <= 0) return false;
            totalRead += read;
        }
        return true;
    }

    public void Stop()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        foreach (var connection in _connections.Keys)
        {
            try { connection.Dispose(); } catch { }
        }
        if (Thread.CurrentThread != _listenThread)
            _listenThread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
