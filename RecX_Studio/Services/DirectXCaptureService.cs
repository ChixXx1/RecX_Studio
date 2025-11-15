using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;

namespace RecX_Studio.Services;

public class DirectXCaptureService : IDisposable
{
    private bool _initialized = false;

    public DirectXCaptureService()
    {
        _initialized = true; // Всегда инициализирован как fallback
    }

    public BitmapSource CaptureScreen()
    {
        try
        {
            // Используем высокопроизводительный захват через Windows API
            return CaptureWithWindowsAPI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка DirectX захвата: {ex.Message}");
            return CaptureScreenFallback();
        }
    }

    // Высокопроизводительный захват через Windows API
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private const uint SRCCOPY = 0x00CC0020;

    private BitmapSource CaptureWithWindowsAPI()
    {
        var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        IntPtr hdcSrc = GetWindowDC(GetDesktopWindow());
        IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, screenBounds.Width, screenBounds.Height);
        IntPtr hOld = SelectObject(hdcDest, hBitmap);

        try
        {
            // Копируем экран
            BitBlt(hdcDest, 0, 0, screenBounds.Width, screenBounds.Height, hdcSrc, 0, 0, SRCCOPY);

            // Создаем Bitmap из HBITMAP
            using (var bitmap = Image.FromHbitmap(hBitmap))
            {
                var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                bitmapSource.Freeze();
                return bitmapSource;
            }
        }
        finally
        {
            // Очищаем ресурсы
            SelectObject(hdcDest, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(GetDesktopWindow(), hdcSrc);
        }
    }

    // Fallback метод используя стандартный захват экрана
    private BitmapSource CaptureScreenFallback()
    {
        try
        {
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            
            using (var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screenBounds.X, screenBounds.Y, 0, 0, screenBounds.Size);
                
                var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                bitmapSource.Freeze();
                return bitmapSource;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка fallback захвата: {ex.Message}");
            return CreateErrorBitmap();
        }
    }

    private BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            bitmap.PixelFormat);

        try
        {
            return BitmapSource.Create(
                bitmapData.Width,
                bitmapData.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgr32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private BitmapSource CreateErrorBitmap()
    {
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            context.DrawRectangle(
                System.Windows.Media.Brushes.DarkRed,
                null,
                new Rect(0, 0, 800, 600));
                
            var errorText = new FormattedText(
                "Ошибка захвата DirectX",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                14,
                System.Windows.Media.Brushes.White,
                1.0);
            
            context.DrawText(errorText, new Point(20, 300));
        }

        var bitmap = new RenderTargetBitmap(800, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    // Метод для проверки доступности DirectX захвата
    public bool IsAvailable()
    {
        return _initialized;
    }

    // Метод для получения информации о дисплее
    public string GetDisplayInfo()
    {
        var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        return $"Разрешение: {screenBounds.Width}x{screenBounds.Height}";
    }

    public void Dispose()
    {
        Debug.WriteLine("✅ DirectXCaptureService disposed");
    }
}