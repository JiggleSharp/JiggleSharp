using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace JiggleSharp.Helpers;

public class TimeSpanLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double dblValue) return string.Empty;
        
        var ts = TimeSpan.FromSeconds(dblValue);
        return ts.TotalSeconds < 60
            ? $"{(int)ts.TotalSeconds}s"
            : ts.Seconds == 0
                ? $"{(int)ts.TotalMinutes}m"
                : $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}