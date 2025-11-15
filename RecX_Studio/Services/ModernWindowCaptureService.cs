using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;

namespace RecX_Studio.Services;

public class ModernWindowCaptureService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
        public bool IsVisible { get; set; }
        public RECT Rect { get; set; }
    }

    public List<WindowInfo> GetAvailableWindows()
    {
        var windows = new List<WindowInfo>();
        var shellWindow = GetShellWindow();

        EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == shellWindow || !IsWindowVisible(hWnd))
                return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var builder = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);

            var title = builder.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            if (title.Contains("Default IME") || title.Contains("MSCTFIME UI"))
                return true;

            string processName = "Unknown";
            RECT rect = new RECT();
            try
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
                
                if (processName == "ApplicationFrameHost" || processName == "ShellExperienceHost")
                    return true;

                if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT))) != 0)
                {
                    if (!GetWindowRect(hWnd, out rect))
                    {
                        return true;
                    }
                }

                if (rect.Width <= 10 || rect.Height <= 10)
                    return true;

            }
            catch
            {
                return true;
            }

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = $"{title} [{processName}]",
                ProcessName = processName,
                IsVisible = true,
                Rect = rect
            });

            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(w => w.Title).ToList();
    }

    public bool IsWindowMinimized(IntPtr hWnd)
    {
        return IsIconic(hWnd);
    }

    public ImageSource CaptureWindow(IntPtr hWnd)
    {
        try
        {
            RECT windowRect;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out windowRect, Marshal.SizeOf(typeof(RECT))) != 0)
            {
                if (!GetWindowRect(hWnd, out windowRect))
                {
                    Debug.WriteLine("❌ Не удалось получить координаты окна");
                    return CreateFallbackImage(hWnd, "Не удалось получить координаты окна");
                }
            }

            if (windowRect.Width <= 10 || windowRect.Height <= 10)
            {
                Debug.WriteLine($"❌ Некорректные размеры окна: {windowRect.Width}x{windowRect.Height}");
                return CreateFallbackImage(hWnd, $"Некорректные размеры окна: {windowRect.Width}x{windowRect.Height}");
            }

            Debug.WriteLine($"🎯 Захват окна {hWnd}: {windowRect.Width}x{windowRect.Height} at ({windowRect.Left}, {windowRect.Top})");

            var dpiX = 96.0;
            var dpiY = 96.0;
            
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = graphics.DpiX;
                dpiY = graphics.DpiY;
            }

            double scaleX = dpiX / 96.0;
            double scaleY = dpiY / 96.0;
            
            int scaledX = (int)(windowRect.Left * scaleX);
            int scaledY = (int)(windowRect.Top * scaleY);
            int scaledWidth = (int)(windowRect.Width * scaleX);
            int scaledHeight = (int)(windowRect.Height * scaleY);

            // --- НОВАЯ ПРОВЕРКА ---
            if (scaledWidth <= 0 || scaledHeight <= 0)
            {
                Debug.WriteLine($"❌ Некорректные масштабированные размеры окна: {scaledWidth}x{scaledHeight}");
                return CreateFallbackImage(hWnd, $"Некорректные размеры окна: {scaledWidth}x{scaledHeight}");
            }
            // --- КОНЕЦ НОВОЙ ПРОВЕРКИ ---

            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using (var screenBitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(screenBitmap))
            {
                graphics.CopyFromScreen(screenBounds.X, screenBounds.Y, 0, 0, screenBounds.Size);

                if (windowRect.Left < screenBounds.Left || windowRect.Top < screenBounds.Top ||
                    windowRect.Right > screenBounds.Right || windowRect.Bottom > screenBounds.Bottom)
                {
                    Debug.WriteLine("⚠️ Окно частично выходит за пределы экрана");
                }

                int cropX = Math.Max(0, scaledX - screenBounds.Left);
                int cropY = Math.Max(0, scaledY - screenBounds.Top);
                int cropWidth = Math.Min(scaledWidth, screenBounds.Width - cropX);
                int cropHeight = Math.Min(scaledHeight, screenBounds.Height - cropY);

                if (cropWidth <= 0 || cropHeight <= 0)
                {
                    Debug.WriteLine("❌ Область обрезки имеет нулевые размеры");
                    return CreateFallbackImage(hWnd, "Область обрезки имеет нулевые размеры");
                }

                using (var croppedBitmap = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb))
                using (var croppedGraphics = Graphics.FromImage(croppedBitmap))
                {
                    croppedGraphics.DrawImage(screenBitmap, 
                        new Rectangle(0, 0, cropWidth, cropHeight),
                        new Rectangle(cropX, cropY, cropWidth, cropHeight),
                        GraphicsUnit.Pixel);

                    var targetSize = new System.Drawing.Size(1024, 576);
                    var scaledBitmap = ScaleBitmap(croppedBitmap, targetSize);
                    var bitmapSource = ConvertBitmapToBitmapSource(scaledBitmap);
                    bitmapSource.Freeze();
                    
                    Debug.WriteLine($"✅ Успешно захвачено окно: {cropWidth}x{cropHeight} -> {targetSize.Width}x{targetSize.Height}");
                    return bitmapSource;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка захвата окна {hWnd}: {ex.Message}");
            return CreateFallbackImage(hWnd, $"Ошибка захвата: {ex.Message}");
        }
    }

    private ImageSource CreateFallbackImage(IntPtr hWnd, string reason)
    {
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            var gradient = new LinearGradientBrush(
                Colors.DarkBlue, Colors.DarkSlateBlue, 
                new Point(0, 0), new Point(1, 1));
            context.DrawRectangle(gradient, null, new Rect(0, 0, 800, 450));
            
            var titleText = new FormattedText(
                $"Окно: 0x{hWnd:X8}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                18,
                Brushes.White,
                1.0);
            context.DrawText(titleText, new Point(20, 30));

            var reasonText = new FormattedText(
                $"Причина: {reason}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                14,
                Brushes.Yellow,
                1.0);
            context.DrawText(reasonText, new Point(20, 70));

            var infoText = new FormattedText(
                "Советы по улучшению захвата:\n\n" +
                "• Убедитесь, что окно не минимизировано\n" +
                "• Разверните окно на передний план\n" +
                "• Проверьте, что окно видимо на экране\n" +
                "• Для некоторых приложений может потребоваться\n  запуск от имени администратора",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                12,
                Brushes.LightGray,
                1.0);
            context.DrawText(infoText, new Point(20, 120));

            context.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(10, 10, 780, 430));
        }
        
        var bitmap = new RenderTargetBitmap(800, 450, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private Bitmap ScaleBitmap(Bitmap original, System.Drawing.Size targetSize)
    {
        var scaled = new Bitmap(targetSize.Width, targetSize.Height);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            
            graphics.DrawImage(original, 
                new Rectangle(0, 0, targetSize.Width, targetSize.Height),
                new Rectangle(0, 0, original.Width, original.Height),
                GraphicsUnit.Pixel);
        }
        return scaled;
    }

    private BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, 
            bitmap.PixelFormat);

        try
        {
            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, 
                bitmapData.Stride);
                
            return bitmapSource;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}