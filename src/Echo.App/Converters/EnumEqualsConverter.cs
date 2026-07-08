using System.Globalization;
using Avalonia.Data.Converters;

namespace echo.App.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum enumValue && parameter is Enum enumParam && enumValue.Equals(enumParam);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
