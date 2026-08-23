using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace OCR_WinApp.Converters;

public sealed class DonutSliceGeometryConverter : IValueConverter
{
    private const double Center = 104;
    private const double OuterRadius = 94;
    private const double InnerRadius = 56;
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

        var startAngle = string.Equals(slice, "used", StringComparison.OrdinalIgnoreCase)
            ? StartAngle + remainingSweep
            : StartAngle;
        var sweepAngle = string.Equals(slice, "used", StringComparison.OrdinalIgnoreCase)
            ? 360 - remainingSweep
            : remainingSweep;

        return CreateSliceGeometry(startAngle, sweepAngle);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
    }

    private static Geometry CreateSliceGeometry(double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0)
            return Geometry.Empty;

        sweepAngle = Math.Min(sweepAngle, 359.99);

        var endAngle = startAngle + sweepAngle;
        var outerStart = PointOnCircle(OuterRadius, startAngle);
        var outerEnd = PointOnCircle(OuterRadius, endAngle);
        var innerEnd = PointOnCircle(InnerRadius, endAngle);
        var innerStart = PointOnCircle(InnerRadius, startAngle);
        var isLargeArc = sweepAngle > 180;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = outerEnd,
            Size = new Size(OuterRadius, OuterRadius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });
        figure.Segments.Add(new LineSegment { Point = innerEnd });
        figure.Segments.Add(new ArcSegment
        {
            Point = innerStart,
            Size = new Size(InnerRadius, InnerRadius),
            SweepDirection = SweepDirection.Counterclockwise,
            IsLargeArc = isLargeArc
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(Center + radius * Math.Cos(radians), Center + radius * Math.Sin(radians));
    }
}
