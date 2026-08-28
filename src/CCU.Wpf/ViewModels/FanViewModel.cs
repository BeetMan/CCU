using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace CCU.Wpf.ViewModels;

public partial class FanViewModel : ObservableObject
{
    [ObservableProperty] private int _fanMode;
    [ObservableProperty] private bool _independentControl;

    // 用 ObservableObject 的 FanPoint 代替 WPF DependencyObject
    [ObservableProperty] private ObservableCollection<FanPoint> _cpuCurve = new(CreateDefaultCurve());
    [ObservableProperty] private ObservableCollection<FanPoint> _gpuCurve = new(CreateDefaultCurve());

    // 通知视图重绘曲线的标志
    [ObservableProperty] private long _cpuVersion;
    [ObservableProperty] private long _gpuVersion;

    partial void OnCpuCurveChanged(ObservableCollection<FanPoint> value)
    {
        if (value != null) value.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CpuCurve)); CpuVersion++; };
        CpuVersion++;
    }
    partial void OnGpuCurveChanged(ObservableCollection<FanPoint> value)
    {
        if (value != null) value.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(GpuCurve)); GpuVersion++; };
        GpuVersion++;
    }

    public bool IsAuto { get => FanMode == 0; set { if (value) FanMode = 0; } }
    public bool IsManual { get => FanMode == 1; set { if (value) FanMode = 1; } }
    public bool IsMax { get => FanMode == 2; set { if (value) FanMode = 2; } }

    [RelayCommand] private void AddCpuPoint() => CpuCurve.Add(new FanPoint(50, 50));
    [RelayCommand] private void AddGpuPoint() => GpuCurve.Add(new FanPoint(50, 50));
    [RelayCommand] private void RemoveCpuPoint(FanPoint? p) { if (p != null && CpuCurve.Count > 1) CpuCurve.Remove(p); }
    [RelayCommand] private void RemoveGpuPoint(FanPoint? p) { if (p != null && GpuCurve.Count > 1) GpuCurve.Remove(p); }
    [RelayCommand] private void ClearCpuCurve() { CpuCurve.Clear(); CpuCurve.Add(new(0, 0)); CpuCurve.Add(new(100, 100)); }
    [RelayCommand] private void ClearGpuCurve() { GpuCurve.Clear(); GpuCurve.Add(new(0, 0)); GpuCurve.Add(new(100, 100)); }

    [RelayCommand] private void SetMode(string mode) => FanMode = mode switch { "auto" => 0, "manual" => 1, "max" => 2, _ => FanMode };

    public PointCollection BuildCurvePoints(ObservableCollection<FanPoint> points)
    {
        var sorted = points.OrderBy(p => p.Temperature).ToList();
        var pc = new PointCollection();
        const double pad = 20, w = 360, h = 180;
        foreach (var p in sorted)
        {
            double x = pad + (p.Temperature / 100.0) * (w - pad);
            double y = h - pad - (p.Duty / 100.0) * (h - pad);
            pc.Add(new Point(x, y));
        }
        return pc;
    }

    public PointCollection BuildFillPoints(ObservableCollection<FanPoint> points)
    {
        var pc = BuildCurvePoints(points);
        const double w = 360, h = 180;
        // 底边两点关闭填充区域
        if (pc.Count > 0) pc.Add(new Point(pc[pc.Count - 1].X, h));
        pc.Add(new Point(0, h));
        return pc;
    }

    private static List<FanPoint> CreateDefaultCurve() => new()
    {
        new(0, 0), new(46, 30), new(52, 35), new(56, 40),
        new(60, 50), new(64, 55), new(68, 60), new(72, 70),
        new(76, 80), new(80, 90), new(85, 100)
    };
}

// ==============================================
// 风扇曲线节点 — 纯 MVVM ObservableObject
// ==============================================
public partial class FanPoint : ObservableObject
{
    [ObservableProperty] private int _temperature;
    [ObservableProperty] private int _duty;

    public FanPoint() { }
    public FanPoint(int temp, int duty) { _temperature = temp; _duty = duty; }

    // Canvas 绑定位
    public double CanvasX => 20 + (Temperature / 100.0) * 340;
    public double CanvasY => 160 - (Duty / 100.0) * 160;

    partial void OnTemperatureChanged(int value) { OnPropertyChanged(nameof(CanvasX)); }
    partial void OnDutyChanged(int value) { OnPropertyChanged(nameof(CanvasY)); }
}
