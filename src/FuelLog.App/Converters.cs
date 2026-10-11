using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FuelLog.Core.Model;

namespace FuelLog.App;

public sealed class DateOnlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : Binding.DoNothing;
}

/// <summary>Перечисление ↔ индекс (для SelectedIndex списков и вкладок).</summary>
public sealed class EnumIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? System.Convert.ToInt32(e, CultureInfo.InvariantCulture) : -1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i >= 0 ? Enum.ToObject(targetType, i) : Binding.DoNothing;
}

public sealed class FuelNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FuelType f ? f.ShortName() : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>true/непустое значение → Visible. Параметр «invert» меняет смысл.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            bool b => b,
            int i => i != 0,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "invert")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Ввод чисел в таблицах: принимает и запятую, и точку, отрицательные значения запрещены.
/// При ошибке ввода значение не меняется, ячейка подсвечивается.
/// </summary>
public sealed class InputNumberConverter : IValueConverter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double d => d.ToString("0.###", Ru),
        int i => i.ToString(Ru),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? string.Empty).Trim().Replace(" ", "").Replace('.', ',');
        if (text.Length == 0)
            text = "0";

        if (targetType == typeof(int))
            return int.TryParse(text, NumberStyles.Integer, Ru, out var i) && i >= 0
                ? i
                : DependencyProperty.UnsetValue;

        return double.TryParse(text, NumberStyles.Float, Ru, out var d) && d >= 0 && double.IsFinite(d)
            ? d
            : DependencyProperty.UnsetValue;
    }
}

/// <summary>Число с заданным количеством знаков; ноль показывается как «—», чтобы таблица читалась легче.</summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d)
            return string.Empty;
        if (Math.Abs(d) < 0.0000001)
            return "—";
        var format = parameter as string ?? "N2";
        return d.ToString(format, CultureInfo.GetCultureInfo("ru-RU"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
