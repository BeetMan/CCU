using System.IO.Pipes;
using System.Text;

// MinimalPipe — 用原始 byte[] 读写避免 ReadLine 的潜在问题

const string PIPE = "CCU.Service.Pipe";
var ready = new ManualResetEventSlim(false);
string? serverResult = null;

var serverTask = Task.Run(() =>
{
    try
    {
        using var s = new NamedPipeServerStream(PIPE, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
        ready.Set();
        s.WaitForConnection();
        byte[] buf = new byte[256];
        int n = s.Read(buf, 0, 256);
        string msg = Encoding.UTF8.GetString(buf, 0, n);
        Console.WriteLine($"[Server] got: {msg}");
        byte[] resp = Encoding.UTF8.GetBytes("pong");
        s.Write(resp, 0, resp.Length);
        s.Flush();
        s.Disconnect();
        serverResult = $"ok (got: {msg})";
    }
    catch (Exception ex) { serverResult = $"err: {ex.Message}"; }
});

ready.Wait(2000);
Thread.Sleep(100);

using var c = new NamedPipeClientStream(".", PIPE, PipeDirection.InOut);
c.Connect(5000);
Console.WriteLine("[Client] connected");

byte[] req = Encoding.UTF8.GetBytes("ping");
c.Write(req, 0, req.Length);
c.Flush();
Console.WriteLine("[Client] sent: ping");

c.WaitForPipeDrain();
byte[] rbuf = new byte[256];
int rn = c.Read(rbuf, 0, rbuf.Length);
string resp = Encoding.UTF8.GetString(rbuf, 0, rn);
Console.WriteLine($"[Client] got: {resp}");

serverTask.Wait(3000);

if (resp == "pong" && serverResult?.StartsWith("ok") == true)
{
    Console.WriteLine();
    Console.WriteLine("✅ Named Pipe IPC 验证通过");
    Console.WriteLine($"   Server result: {serverResult}");
}
else
    Console.WriteLine($"❌ FAIL — server={serverResult} client={resp}");
