using System.Globalization;

namespace BengiDevTools.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "OK" => Color.FromArgb("#4EC9B0"),
            "FAILED" => Color.FromArgb("#F44747"),
            "Bygger..." => Color.FromArgb("#9CDCFE"),
            _ => Color.FromArgb("#858585"),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
