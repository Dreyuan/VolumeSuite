using System;
using System.Globalization;
using System.Windows.Data;
using MahApps.Metro.IconPacks;

namespace VolumeSuite.Converters;

public class StringToLucideKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrEmpty(s)) return PackIconLucideKind.Circle;

        return Enum.TryParse<PackIconLucideKind>(s, ignoreCase: true, out var k)
            ? k
            : PackIconLucideKind.Circle;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}