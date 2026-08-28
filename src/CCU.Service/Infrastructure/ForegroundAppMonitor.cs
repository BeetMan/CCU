using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 前台窗口进程名监视器（轻量轮询，1s 一次，无钩子无注入）。
/// </summary>
public sealed class ForegroundAppMonitor
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd); // 保留引用避免裁剪

    /// <summary>获取当前前台窗口的进程名（小写，含 .exe）；失败返回 null。</summary>
    public static string? GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return null;

            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return string.IsNullOrWhiteSpace(name) ? null : name.ToLowerInvariant() + ".exe";
        }
        catch
        {
            // 进程可能刚退出（PID 失效）— 返回 null 即可
            return null;
        }
    }
}
