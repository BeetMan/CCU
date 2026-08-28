using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CCU.Wpf.Views;

/// <summary>
/// OSD 覆盖窗口 — 性能模式切换时短暂显示
/// 全屏透明，居中浮层，1.5 秒淡入淡出
/// </summary>
public partial class OsdWindow : Window
{
    private readonly int _mode;

    public OsdWindow(int mode)
    {
        _mode = mode;
        InitializeComponent();
        SetupContent();
    }

    private void SetupContent()
    {
        var (icon, name, desc, color) = _mode switch
        {
            0 => ("📋", "办公模式", "静音散热 · 电池续航优先", Color.FromRgb(0x94, 0xA3, 0xB8)),
            1 => ("🎮", "游戏模式", "均衡性能 · 智能风扇调节", Color.FromRgb(0x00, 0xD4, 0xAA)),
            2 => ("🔥", "狂暴模式", "满血释放 · 风扇极速运转", Color.FromRgb(0xF9, 0x73, 0x16)),
            _ => ("⚡", "自定义模式", "手动调校 · 按需分配", Color.FromRgb(0x0E, 0xA5, 0xE9))
        };

        IconText.Text = icon;
        ModeName.Text = name;
        ModeDesc.Text = desc;
        DataContext = new { AccentBrush = new SolidColorBrush(color) };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var sb = (Storyboard)FindResource("FadeInOut");
        sb.Completed += (_, _) => Close();
        BeginStoryboard(sb);
    }
}
