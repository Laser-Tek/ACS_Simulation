using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AcsHal.Viewer;

/// <summary>Maps a 0..1 fraction to pixels. The parameter is the length; a negative length draws from the far end (for Y, so up is +).</summary>
public sealed class FractionToPixelsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var length = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        var fraction = value is double f ? f : 0;
        return length >= 0 ? fraction * length : (1 - fraction) * -length;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Lamp colour: the parameter colour when true, grey when false.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly Brush Off = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? new BrushConverter().ConvertFromInvariantString((string)parameter)! : Off;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string path ? Path.GetFileName(path) : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
