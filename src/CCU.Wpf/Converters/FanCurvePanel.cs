using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using CCU.Wpf.ViewModels;

namespace CCU.Wpf.Converters;

public class FanCurvePanel : ContentControl
{
    static FanCurvePanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FanCurvePanel),
            new FrameworkPropertyMetadata(typeof(FanCurvePanel)));
    }

    public static readonly DependencyProperty CurveDataProperty =
        DependencyProperty.Register(nameof(CurveData), typeof(System.Collections.IList),
            typeof(FanCurvePanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(FanCurvePanel),
            new PropertyMetadata(Color.FromRgb(0x00, 0xD4, 0xAA)));
    public static readonly DependencyProperty FillColorProperty =
        DependencyProperty.Register(nameof(FillColor), typeof(Color), typeof(FanCurvePanel),
            new PropertyMetadata(Color.FromArgb(0x22, 0x00, 0xD4, 0xAA)));

    public System.Collections.IList? CurveData { get => (System.Collections.IList?)GetValue(CurveDataProperty); set => SetValue(CurveDataProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public Color FillColor { get => (Color)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }

    private const double W = 360, H = 180, Pad = 20;
    private Polyline? _line;
    private Polygon? _fill;
    private ItemsControl? _nodes;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _line = (Polyline?)GetTemplateChild("PART_Line");
        _fill = (Polygon?)GetTemplateChild("PART_Fill");
        _nodes = (ItemsControl?)GetTemplateChild("PART_Nodes");

        Loaded += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CurveDataProperty) Rebuild();
        if (e.Property == LineColorProperty && _line != null) _line.Stroke = new SolidColorBrush(LineColor);
        if (e.Property == FillColorProperty && _fill != null) _fill.Fill = new SolidColorBrush(FillColor);
    }

    private void Rebuild()
    {
        if (_line == null || _fill == null) return;

        var points = CurveData?.Cast<FanPoint>().OrderBy(p => p.Temperature).ToList();
        if (points == null || points.Count == 0)
        {
            // 无数据时显示默认引导线
            _line.Points = new PointCollection { new Point(0, 160), new Point(180, 80), new Point(360, 0) };
            _fill.Points = new PointCollection { new Point(0, 160), new Point(180, 80), new Point(360, 0), new Point(360, 180), new Point(0, 180) };
            _line.Stroke = new SolidColorBrush(LineColor);
            _fill.Fill = new SolidColorBrush(FillColor);
            if (_nodes != null) _nodes.ItemsSource = null;
            return;
        }

        var linePts = new PointCollection();
        var fillPts = new PointCollection();

        foreach (var p in points)
        {
            double x = Pad + (p.Temperature / 100.0) * (W - Pad);
            double y = H - Pad - (p.Duty / 100.0) * (H - Pad);
            linePts.Add(new Point(x, y));
            fillPts.Add(new Point(x, y));
        }

        // 边界处理
        linePts.Insert(0, new Point(0, linePts[0].Y));
        linePts.Add(new Point(W, linePts[linePts.Count - 1].Y));
        fillPts.Insert(0, new Point(0, fillPts[0].Y));
        fillPts.Add(new Point(W, H));
        fillPts.Add(new Point(0, H));

        _line.Points = linePts;
        _line.Stroke = new SolidColorBrush(LineColor);
        _fill.Points = fillPts;
        _fill.Fill = new SolidColorBrush(FillColor);

        if (_nodes != null) _nodes.ItemsSource = points;
    }

    public static void UpdateThumbPosition(FrameworkElement el, FanPoint pt)
    {
        double x = Pad + (pt.Temperature / 100.0) * (W - Pad);
        double y = H - Pad - (pt.Duty / 100.0) * (H - Pad);
        Canvas.SetLeft(el, x - 7);
        Canvas.SetTop(el, y - 7);
    }
}
