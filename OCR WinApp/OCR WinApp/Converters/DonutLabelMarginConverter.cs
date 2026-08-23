using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OCR_WinApp.Converters;

public sealed class DonutLabelMarginConverter : IValueConverter
{
    private const double Center = 104;
    private const double LabelRadius = 75;
    private const double LabelWidth = 84;
    private const double LabelHeight = 30;
    private const double StartAngle = -90;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var remainingPercent = value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            decimal decimalValue => (double)decimalValue,
            _ => 0
        };

        remainingPercent = Math.Clamp(remainingPercent, 0, 100);
        var remainingSweep = remainingPercent / 100 * 360;
        var slice = parameter as string;
        var midAngle = string.Equals(slice, "used", StringComparison.OrdinalIgnoreCase)
            ? StartAngle + remainingSweep + (360 - remainingSweep) / 2
            : StartAngle + remainingSweep / 2;

        var radians = midAngle * Math.PI / 180;
        var left = Center + LabelRadius * Math.Cos(radians) - LabelWidth / 2;
        var top = Center + LabelRadius * Math.Sin(radians) - LabelHeight / 2;
        return new Thickness(left, top, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return DependencyProperty.UnsetValue;
    }
}
