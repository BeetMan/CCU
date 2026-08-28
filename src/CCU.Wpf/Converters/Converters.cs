using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace CCU.Wpf.Converters;

public class BoolToInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value != null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.Equals(parameter) == true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

public class TemperatureToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 总是返回 Brush (Binding to Foreground)
        double temp = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => double.NaN
        };

        if (double.IsNaN(temp))
            return new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA));

        Color c = temp >= 90 ? Color.FromRgb(0xEF, 0x44, 0x44)
               : temp >= 75 ? Color.FromRgb(0xF9, 0x73, 0x16)
               : temp >= 60 ? Color.FromRgb(0xF5, 0x9E, 0x0B)
               : Color.FromRgb(0x00, 0xD4, 0xAA);

        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class UsageToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => double.NaN
        };

        Color c = double.IsNaN(pct) ? Color.FromRgb(0x00, 0xD4, 0xAA)
               : pct >= 90 ? Color.FromRgb(0xEF, 0x44, 0x44)
               : pct >= 70 ? Color.FromRgb(0xF5, 0x9E, 0x0B)
               : Color.FromRgb(0x0E, 0xA5, 0xE9);

        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DoubleToIntStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? d.ToString("F0") : "0";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DutyToHeightConverter : IValueConverter
{
    // 风扇曲线图上的 duty → 高度 (最大值 = 160px)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int duty) return 0.0;
        return duty / 100.0 * 160.0;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TempToXConverter : IValueConverter
{
    // 风扇曲线图上的温度 → X (0-100°C → 0-320px)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int temp) return 0.0;
        return temp / 100.0 * 320.0;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 比较 int Value 是否等于 ConverterParameter (int) — 用于 GpuView RadioButton 等
/// </summary>
public class IntEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intVal && parameter is string str && int.TryParse(str, out int param))
            return intVal == param;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string str && int.TryParse(str, out int param))
            return param;
        return Binding.DoNothing;
    }
}

public class MultiParamConverter : MarkupExtension, IValueConverter
{
    // 检查值是否等于 ConverterParameter
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
    public override object ProvideValue(IServiceProvider sp) => this;
}

/// <summary>
/// GPU OC 开关按钮参数：当前偏移 >0 返回 "off"（点击则关闭），否则 "on"
/// </summary>
public class OcToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int offset && offset > 0 ? "off" : "on";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
