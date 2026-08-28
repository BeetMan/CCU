using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;

namespace CCU.Wpf;

public partial class App : Application
{
    private WpfNotifyIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 手动创建 MainWindow (因为移除了 StartupUri)
        MainWindow = new MainWindow();
        MainWindow.Show();

        _notifyIcon = new WpfNotifyIcon(MainWindow)
        {
            ToolTip = "智控中心 CCU",
            Visible = true
        };
        _notifyIcon.SetIcon(WpfNotifyIcon.CreateCircleIcon(Color.FromArgb(0x94, 0xA3, 0xB8)));
        _notifyIcon.SetDoubleClick(() =>
        {
            MainWindow?.Show();
            MainWindow!.WindowState = WindowState.Normal;
            MainWindow.Activate();
        });

        _notifyIcon.SetContextMenu([
            new MenuItem
            {
                Header = "打开智控中心",
                Command = new RelayCommand(() =>
                {
                    MainWindow?.Show();
                    MainWindow!.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                })
            },
            new Separator(),
            new MenuItem
            {
                Header = "退出",
                Command = new RelayCommand(Shutdown)
            }
        ]);

        ModeChanged += (mode) =>
        {
            var color = mode switch
            {
                0 => Color.FromArgb(0x94, 0xA3, 0xB8),
                1 => Color.FromArgb(0x00, 0xD4, 0xAA),
                2 => Color.FromArgb(0xF9, 0x73, 0x16),
                _ => Color.FromArgb(0x0E, 0xA5, 0xE9)
            };
            _notifyIcon?.SetIcon(WpfNotifyIcon.CreateCircleIcon(color));
            ShowOsd(mode);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    internal static int CurrentTrayMode { get; set; }

    public static event Action<int>? ModeChanged;

    public static void NotifyModeChanged(int mode)
    {
        CurrentTrayMode = mode;
        ModeChanged?.Invoke(mode);
    }

    internal static void ShowOsd(int mode)
    {
        Current.Dispatcher.Invoke(() =>
        {
            var osd = new Views.OsdWindow(mode);
            osd.Show();
        });
    }
}
