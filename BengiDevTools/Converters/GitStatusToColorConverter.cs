using System.Globalization;

namespace BengiDevTools.Converters;

public class GitStatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Uppdaterad" => Color.FromArgb("#4EC9B0"),
            "Bakom"      => Color.FromArgb("#CE9178"),
            "Saknas"     => Color.FromArgb("#F44747"),
            _            => Color.FromArgb("#858585"),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
