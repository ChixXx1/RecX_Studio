using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RecX_Studio.Utils;

public class ZoomConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double zoomLevel)
        {
            return new ScaleTransform(zoomLevel, zoomLevel);
        }
        return new ScaleTransform(1, 1);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
