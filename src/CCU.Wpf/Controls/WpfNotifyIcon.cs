using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CCU.Wpf;

/// <summary>
/// WPF 托盘图标 — 纯 P/Invoke Shell_NotifyIcon
/// 完全不依赖 WinForms，避免命名空间冲突
/// </summary>
public sealed class WpfNotifyIcon : IDisposable
{
    private readonly int _uid;
    private readonly Window _owner;
    private Icon? _icon;
    private Action? _onDoubleClick;
    private ContextMenu? _menu;
    private bool _visible;
    private bool _disposed;

    public string ToolTip { get; set; } = "";
    public bool Visible
    {
        get => _visible;
        set { _visible = value; Refresh(); }
    }

    public WpfNotifyIcon(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _uid = GenerateUid();
        owner.Closed += (_, _) => Dispose();

        // 延迟挂钩 — 等窗口完全加载后 Source 才可用
        owner.Loaded += (_, _) =>
        {
            var source = PresentationSource.FromVisual(owner) as HwndSource;
            source?.AddHook(WndProc);
            if (_visible) Refresh(); // 首次添加托盘图标
        };
    }

    public void SetIcon(Icon icon) { _icon = icon; if (_visible) Refresh(); }
    public void SetDoubleClick(Action handler) => _onDoubleClick = handler;

    public void SetContextMenu(Control[] items)
    {
        // 右键弹出 ContextMenu
        _menu = new ContextMenu();
        foreach (var item in items)
            _menu.Items.Add(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visible = false;
        Refresh();
        _icon?.Dispose();
    }

    private void Refresh()
    {
        if (_disposed) return;

        var hwnd = new WindowInteropHelper(_owner).Handle;
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = _uid,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYMSG,
            hIcon = _icon?.Handle ?? IntPtr.Zero,
            szTip = ToolTip.Length > 128 ? ToolTip[..128] : ToolTip
        };

        Shell_NotifyIcon(_visible ? NIM_ADD : NIM_DELETE, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYMSG && (int)wParam == _uid)
        {
            switch ((int)lParam)
            {
                case WM_LBUTTONDBLCLK:
                    _onDoubleClick?.Invoke();
                    handled = true;
                    break;
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_menu == null) return;
        _menu.IsOpen = true;
    }

    // ======== P/Invoke ========

    private const int NIM_ADD = 0, NIM_DELETE = 2;
    private const int NIF_ICON = 2, NIF_MESSAGE = 1, NIF_TIP = 4;
    private const int WM_TRAYMSG = 0x0400 + 1024;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("shell32.dll")]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    private static int GenerateUid() => new Random().Next(10000, int.MaxValue);

    /// <summary>
    /// 辅助: 创建纯色圆形 Icon
    /// </summary>
    public static Icon CreateCircleIcon(System.Drawing.Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, 28, 28);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
