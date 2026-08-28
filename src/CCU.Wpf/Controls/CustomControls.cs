using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CCU.Wpf.Controls;

/// <summary>
/// 左侧导航项 — 图标 + 文字，激活态霓虹左边框
/// </summary>
public class NavItem : ContentControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavItem), new PropertyMetadata("•"));
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavItem), new PropertyMetadata(""));
    public static readonly DependencyProperty ParamProperty =
        DependencyProperty.Register(nameof(Param), typeof(string), typeof(NavItem), new PropertyMetadata(""));
    public static readonly DependencyProperty NavCmdProperty =
        DependencyProperty.Register(nameof(NavCmd), typeof(ICommand), typeof(NavItem));
    public static readonly DependencyProperty ActiveProperty =
        DependencyProperty.Register(nameof(Active), typeof(bool), typeof(NavItem), new PropertyMetadata(false, OnActiveChanged));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Param { get => (string)GetValue(ParamProperty); set => SetValue(ParamProperty, value); }
    public ICommand NavCmd { get => (ICommand)GetValue(NavCmdProperty); set => SetValue(NavCmdProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavItem item) return;
        item.Background = item.Active
            ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
            : Brushes.Transparent;
        item.Foreground = item.Active
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA))
            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    }
}

/// <summary>
/// 顶栏性能模式快捷切换 Chip
/// </summary>
public class ModeChip : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ModeChip), new PropertyMetadata(""));
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(string), typeof(ModeChip), new PropertyMetadata(""));
    public static readonly DependencyProperty CmdProperty =
        DependencyProperty.Register(nameof(Cmd), typeof(ICommand), typeof(ModeChip));
    public static readonly DependencyProperty ColorAccentProperty =
        DependencyProperty.Register(nameof(ColorAccent), typeof(Brush), typeof(ModeChip),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public ICommand Cmd { get => (ICommand)GetValue(CmdProperty); set => SetValue(CmdProperty, value); }
    public Brush ColorAccent { get => (Brush)GetValue(ColorAccentProperty); set => SetValue(ColorAccentProperty, value); }

    static ModeChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ModeChip), new FrameworkPropertyMetadata(typeof(ModeChip)));
    }
}

/// <summary>
/// 硬件指标卡片 — 大字数值 + 标签
/// </summary>
public class MetricDisplay : ContentControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricDisplay), new PropertyMetadata("--"));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(MetricDisplay), new PropertyMetadata(""));
    public static readonly DependencyProperty SubLabelProperty =
        DependencyProperty.Register(nameof(SubLabel), typeof(string), typeof(MetricDisplay), new PropertyMetadata(""));
    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Brush), typeof(MetricDisplay),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA))));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string SubLabel { get => (string)GetValue(SubLabelProperty); set => SetValue(SubLabelProperty, value); }
    public Brush AccentColor { get => (Brush)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
}

/// <summary>
/// 风扇曲线绘图控件
/// <summary>
/// 键盘灯效 Chip
/// </summary>
public class EffectChip : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(EffectChip), new PropertyMetadata(""));
    public static readonly DependencyProperty HighlightProperty =
        DependencyProperty.Register(nameof(Highlight), typeof(Brush), typeof(EffectChip),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))));
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(EffectChip), new PropertyMetadata(false));
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(EffectChip));
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(EffectChip));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Brush Highlight { get => (Brush)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public ICommand Command { get => (ICommand)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object CommandParameter { get => (object)GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    static EffectChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EffectChip), new FrameworkPropertyMetadata(typeof(EffectChip)));
    }
}

/// <summary>
/// 显示色彩预设 Chip
/// </summary>
public class DisplayProfileChip : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(DisplayProfileChip), new PropertyMetadata(""));
    public static readonly DependencyProperty DescProperty =
        DependencyProperty.Register(nameof(Desc), typeof(string), typeof(DisplayProfileChip), new PropertyMetadata(""));
    public static readonly DependencyProperty HighlightProperty =
        DependencyProperty.Register(nameof(Highlight), typeof(Brush), typeof(DisplayProfileChip),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))));
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(DisplayProfileChip), new PropertyMetadata(false));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Desc { get => (string)GetValue(DescProperty); set => SetValue(DescProperty, value); }
    public Brush Highlight { get => (Brush)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    static DisplayProfileChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DisplayProfileChip), new FrameworkPropertyMetadata(typeof(DisplayProfileChip)));
    }
}
