using System.Windows;
using System.Windows.Input;
using CCU.Wpf.ViewModels;

namespace CCU.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            // DataContext 由 XAML 设置 (<vm:MainViewModel />)

            Closed += (s, e) =>
            {
                if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
                {
                    var vm = DataContext as MainViewModel;
                    vm?.MinimizeToTrayCommand.Execute(null);
                }
            };
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(@"D:\ccu_crash.log", $"MainWindow ctor: {ex}");
            throw;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // ✕ 按钮 → 退出应用
        Application.Current.Shutdown();
    }
}
