// PipeProbe v3 — 使用同步 NamedPipeServerStream 避免异步死锁
// 在独立 Task 中先启动 server，再主线启动 client

using System.IO.Pipes;
using System.Text;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("╔══════════════════════════════════╗");
Console.WriteLine("║   PipeProbe v3 — IPC 验证       ║");
Console.WriteLine("╚══════════════════════════════════╝");

const string pipeName = "CCU.Service.Pipe";

var serverReady = new ManualResetEventSlim(false);
string? serverResult = null;

// Server thread: 先阻塞等待连接
var serverThread = new Thread(() =>
{
    try
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        serverReady.Set();
        Console.WriteLine("  [Server] 等待连接...");
        server.WaitForConnection();  // 同步阻塞
        Console.WriteLine("  [Server] 已连接");

        using var reader = new StreamReader(server, Encoding.UTF8);
        using var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };

        string? line = reader.ReadLine();
        Console.WriteLine($"  [Server] 收到: {line}");

        writer.WriteLine("{\"type\":\"pong\",\"payload\":\"world\"}");
        Console.WriteLine("  [Server] 回复: pong");

        server.Disconnect();
        serverResult = "ok";
    }
    catch (Exception ex)
    {
        serverResult = $"error: {ex.Message}";
        Console.WriteLine($"  [Server] 错误: {ex.Message}");
    }
})
{ IsBackground = true, Name = "PipeServerThread" };

serverThread.Start();
serverReady.Wait(3000);  // 确保 server 已创建并开始监听
Thread.Sleep(200);       // 给 server 一点时间到达 WaitForConnection

// Client: 连接
try
{
    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    Console.WriteLine("  [Client] 正在连接...");
    client.Connect(3000);  // 同步连接
    Console.WriteLine("  [Client] 已连接");

    using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
    using var reader = new StreamReader(client, Encoding.UTF8);

    writer.WriteLine("{\"type\":\"ping\",\"payload\":\"hello\"}");
    Console.WriteLine("  [Client] 发送: ping");

    string? resp = reader.ReadLine();
    Console.WriteLine($"  [Client] 收到: {resp}");

    serverThread.Join(3000);

    if (resp != null && resp.Contains("pong") && serverResult == "ok")
    {
        Console.WriteLine();
        Console.WriteLine("  ✅ Named Pipe IPC 链路验证通过");
        Console.WriteLine("  ℹ️  全双工通信: Server ↔ Client 正常");
    }
    else
        Console.WriteLine($"  ❌ 验证失败 (server={serverResult}, client={resp})");
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ Client 错误: {ex.Message}");
}

Console.WriteLine("═══════════════════════════════════");
