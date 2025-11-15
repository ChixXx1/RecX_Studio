using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecX_Studio.Utils;

public class EmptyImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value ?? CreateDefaultImage();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private ImageSource CreateDefaultImage()
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(30, 30, 30)), 
                null, 
                new Rect(0, 0, 800, 450));
        }
        
        var bitmap = new RenderTargetBitmap(800, 450, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        return bitmap;
    }
}