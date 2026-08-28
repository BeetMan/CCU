using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 直接通过 CreateFile/DeviceIoControl 与 UWACPIDriver.sys 内核驱动通信
/// 绕过 WMI AcpiTest_MULong 的恒等函数限制，实现真正的 EC 读写
/// </summary>
public class KernelAcpiClient
{
    private readonly ILogger<KernelAcpiClient> _logger;
    private IntPtr _deviceHandle = IntPtr.Zero;
    private readonly object _lock = new();

    // 设备符号链接 — UWACPIDriver 注册的设备路径
    private const string DevicePath = @"\\.\ACPIDriver";

    // SMRW 命令
    public const byte SMRW_CMD_READ = 0xBB;
    public const byte SMRW_CMD_WRITE = 0xAA;

    #region Win32 P/Invoke

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion

    public KernelAcpiClient(ILogger<KernelAcpiClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 打开内核驱动设备
    /// </summary>
    public bool OpenDevice()
    {
        try
        {
            _deviceHandle = CreateFileW(
                DevicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (_deviceHandle == new IntPtr(-1) || _deviceHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to open {Path}: error {Error}", DevicePath, err);
                _deviceHandle = IntPtr.Zero;
                return false;
            }

            _logger.LogInformation("Opened kernel device {Path} — handle 0x{Handle:X}",
                DevicePath, _deviceHandle.ToInt64());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open kernel device {Path}", DevicePath);
            return false;
        }
    }

    /// <summary>
    /// 关闭内核驱动设备
    /// </summary>
    public void CloseDevice()
    {
        if (_deviceHandle != IntPtr.Zero)
        {
            CloseHandle(_deviceHandle);
            _logger.LogInformation("Closed kernel device handle");
            _deviceHandle = IntPtr.Zero;
        }
    }

    public bool IsOpen => _deviceHandle != IntPtr.Zero;

    /// <summary>
    /// 发送 IOCTL 到内核驱动
    /// </summary>
    public (bool success, byte[]? data) SendIoctl(uint ioctlCode, byte[]? input = null)
    {
        if (!IsOpen) throw new InvalidOperationException("Kernel device not open");

        int inSize = input?.Length ?? 0;
        IntPtr inBuf = IntPtr.Zero;
        if (input != null)
        {
            inBuf = Marshal.AllocHGlobal(inSize);
            Marshal.Copy(input, 0, inBuf, inSize);
        }

        const int outSize = 256;
        IntPtr outBuf = Marshal.AllocHGlobal(outSize);

        try
        {
            lock (_lock)
            {
                bool ok = DeviceIoControl(_deviceHandle, ioctlCode,
                    inBuf, (uint)inSize,
                    outBuf, (uint)outSize,
                    out uint bytesReturned, IntPtr.Zero);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    _logger.LogDebug("IOCTL 0x{Code:X8} failed: err={Error}", ioctlCode, err);
                    return (false, null);
                }

                if (bytesReturned > 0)
                {
                    byte[] result = new byte[bytesReturned];
                    Marshal.Copy(outBuf, result, 0, (int)bytesReturned);
                    _logger.LogDebug("IOCTL 0x{Code:X8}: {Bytes} bytes returned",
                        ioctlCode, bytesReturned);
                    return (true, result);
                }

                return (true, Array.Empty<byte>());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(outBuf);
            if (inBuf != IntPtr.Zero) Marshal.FreeHGlobal(inBuf);
        }
    }

    /// <summary>
    /// 读取 EC 寄存器（通过内核驱动 IOCTL + SMRW 编码）
    /// </summary>
    public byte ECRead(ushort addr)
    {
        // SMRW Read 命令: cmd=0xBB << 56 | addr
        ulong smrw = ((ulong)SMRW_CMD_READ << 56) | addr;
        byte[] input = BitConverter.GetBytes(smrw);

        var (ok, data) = SendIoctl(0x00220000, input);
        if (!ok)
            (ok, data) = SendIoctl(0x00220004, input);
        if (!ok)
            (ok, data) = SendIoctl(0x0022000C, input);

        if (ok && data != null && data.Length >= 4)
        {
            // 返回值低字节是 EC 数据
            return data[0];
        }

        _logger.LogWarning("ECRead(0x{Addr:X4}) failed via kernel IOCTL", addr);
        return 0;
    }

    /// <summary>
    /// 写入 EC 寄存器（通过内核驱动 IOCTL + SMRW 编码）
    /// </summary>
    public void ECWrite(ushort addr, byte value)
    {
        // SMRW Write 命令: cmd=0xAA << 56 | value << 32 | addr
        ulong smrw = ((ulong)SMRW_CMD_WRITE << 56) | ((ulong)value << 32) | addr;
        byte[] input = BitConverter.GetBytes(smrw);

        if (!SendIoctl(0x00220000, input).success)
            if (!SendIoctl(0x00220004, input).success)
                SendIoctl(0x0022000C, input);

        _logger.LogDebug("ECWrite(0x{Addr:X4}, 0x{Value:X2}) via kernel IOCTL", addr, value);
    }

    /// <summary>
    /// IOCTL 探测 — 枚举所有可能的 IOCTL 码，找内核驱动的通信接口
    /// </summary>
    public string DiscoverIoctl()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Kernel Driver IOCTL Discovery @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine($"Device: {DevicePath}");
        sb.AppendLine($"Handle: 0x{_deviceHandle.ToInt64():X}");

        // SMRW Read 编码 (读地址 0x04CC = Performance Mode)
        byte[] smrwRead = BitConverter.GetBytes(0xBB000000000004CCUL);

        // 扫描方法: 尝试不同 IOCTL 函数编号
        // DWORDS in driver binary suggest function codes in range 0x800-0x900
        // Also try codes at specific offsets

        sb.AppendLine();
        sb.AppendLine("--- Scanning IOCTL function codes ---");

        for (uint func = 0x800; func <= 0x910; func += 0x10)
        {
            // CTL_CODE(FILE_DEVICE_UNKNOWN, func, METHOD_BUFFERED, FILE_ANY_ACCESS)
            uint code = (0x22 << 16) | (func << 2) | 0;
            var (ok, data) = SendIoctl(code, smrwRead);
            if (ok && data != null && data.Length > 0)
            {
                string hex = BitConverter.ToString(data).Replace("-", " ");
                sb.AppendLine($"  IOCTL 0x{code:X8} (func=0x{func:X3}): OK, {data.Length} bytes: {hex}");
            }
        }

        // 再试 METHOD_IN_DIRECT 和 METHOD_OUT_DIRECT
        sb.AppendLine();
        sb.AppendLine("--- METHOD_IN_DIRECT ---");
        for (uint func = 0x800; func <= 0x900; func += 0x10)
        {
            uint code = (0x22 << 16) | (func << 2) | 1;
            var (ok, data) = SendIoctl(code, smrwRead);
            if (ok && data != null && data.Length > 0)
            {
                string hex = BitConverter.ToString(data).Replace("-", " ");
                sb.AppendLine($"  IOCTL 0x{code:X8}: OK, {data.Length} bytes: {hex}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- METHOD_OUT_DIRECT ---");
        for (uint func = 0x800; func <= 0x900; func += 0x10)
        {
            uint code = (0x22 << 16) | (func << 2) | 2;
            var (ok, data) = SendIoctl(code, smrwRead);
            if (ok && data != null && data.Length > 0)
            {
                string hex = BitConverter.ToString(data).Replace("-", " ");
                sb.AppendLine($"  IOCTL 0x{code:X8}: OK, {data.Length} bytes: {hex}");
            }
        }

        // 试 ReadFile/WriteFile
        sb.AppendLine();
        sb.AppendLine("--- ReadFile test ---");
        try
        {
            int bufSize = 256;
            IntPtr buf = Marshal.AllocHGlobal(bufSize);
            try
            {
                bool ok = ReadFile(_deviceHandle, buf, (uint)bufSize, out uint bytesRead, IntPtr.Zero);
                int err = Marshal.GetLastWin32Error();
                sb.AppendLine($"  ReadFile: ok={ok}, read={bytesRead}, err={err}");
                if (ok && bytesRead > 0)
                {
                    byte[] result = new byte[bytesRead];
                    Marshal.Copy(buf, result, 0, (int)bytesRead);
                    sb.AppendLine($"  Data: {BitConverter.ToString(result).Replace("-", " ")}");
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        // 尝试写入再读回
        sb.AppendLine();
        sb.AppendLine("--- SMRW Write+Read test (addr=0x04CC, value=0xAB) ---");
        try
        {
            ulong smrwWrite = 0xAA000000AB0004CCUL;  // Write 0xAB to 0x04CC
            byte[] writeCmd = BitConverter.GetBytes(smrwWrite);

            // write
            var (wOk, _) = SendIoctl(0x00220000, writeCmd);
            sb.AppendLine($"  Write IOCTL 0x00220000: ok={wOk}");

            // read back
            var (rOk, rData) = SendIoctl(0x00220000, smrwRead);
            if (rOk && rData != null)
                sb.AppendLine($"  Read IOCTL 0x00220000: {BitConverter.ToString(rData).Replace("-", " ")}");
            else
                sb.AppendLine($"  Read IOCTL 0x00220000: ok={rOk}, dataSize={rData?.Length}");
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        // 也试试 WriteFile 方式
        sb.AppendLine();
        sb.AppendLine("--- WriteFile test (SMRW Read via WriteFile) ---");
        try
        {
            IntPtr wBuf = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(smrwRead, 0, wBuf, 8);
                bool ok = WriteFile(_deviceHandle, wBuf, 8, out uint written, IntPtr.Zero);
                int err = Marshal.GetLastWin32Error();
                sb.AppendLine($"  WriteFile: ok={ok}, written={written}, err={err}");
            }
            finally { Marshal.FreeHGlobal(wBuf); }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        sb.AppendLine();
        sb.AppendLine("=== DISCOVERY COMPLETE ===");

        // 写入文件
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                               "CCU_Alternative", "kernel_ioctl_discovery.txt");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());

        return sb.ToString();
    }
}
