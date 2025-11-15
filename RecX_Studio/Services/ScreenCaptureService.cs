using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;

namespace RecX_Studio.Services;

public class ScreenCaptureService : IDisposable
{
    private System.Timers.Timer? _captureTimer;
    private Action<ImageSource>? _onFrameCaptured;
    private bool _isDisposed;
    private int _targetFps = 30; // Увеличиваем по умолчанию
    private volatile bool _isCapturing;
    private IntPtr _currentWindowHandle = IntPtr.Zero;
    private bool _isWindowCapture = false;
    private System.Drawing.Rectangle? _captureArea;
    private readonly AreaSelectionService _areaSelectionService = new AreaSelectionService();
    private readonly ModernWindowCaptureService _windowCaptureService = new ModernWindowCaptureService();

    // Оптимизация: кэш для BitmapSource
    private BitmapSource? _cachedBitmapSource;
    private System.Drawing.Bitmap? _cachedBitmap;

    public event Action<string>? OnCaptureStatusChanged;
    
    public void StartAreaSelection(Action<Rectangle> onAreaSelected)
    {
        var areaSelectionService = new AreaSelectionService();
        areaSelectionService.StartAreaSelection(onAreaSelected);
    }
    
    public void StartCapture(Action<ImageSource> onFrameCaptured, int fps = 30)
    {
        if (_isDisposed) return;
        
        _onFrameCaptured = onFrameCaptured;
        _targetFps = Math.Max(10, Math.Min(fps, 60)); // Минимальный FPS 10 для стабильности
        _isWindowCapture = false;
        _currentWindowHandle = IntPtr.Zero;
        _captureArea = null;
        
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        
        // Оптимизация: используем высокий приоритет таймера
        _captureTimer = new System.Timers.Timer(1000.0 / _targetFps);
        _captureTimer.Elapsed += CaptureFrame;
        _captureTimer.AutoReset = true;
        _captureTimer.Start();
        
        Debug.WriteLine($"🚀 Захват экрана запущен с FPS: {_targetFps}");
        OnCaptureStatusChanged?.Invoke($"Захват запущен ({_targetFps} FPS)");
    }

    public void StartAreaCapture(System.Drawing.Rectangle area, Action<ImageSource> onFrameCaptured, int fps = 30)
    {
        if (_isDisposed) return;
        
        _onFrameCaptured = onFrameCaptured;
        _targetFps = Math.Max(10, Math.Min(fps, 60));
        _isWindowCapture = false;
        _currentWindowHandle = IntPtr.Zero;
        _captureArea = area;
        
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        
        _captureTimer = new System.Timers.Timer(1000.0 / _targetFps);
        _captureTimer.Elapsed += CaptureFrame;
        _captureTimer.AutoReset = true;
        _captureTimer.Start();
        
        Debug.WriteLine($"🖼️ Захват области запущен с FPS: {_targetFps}, Область: {area.Width}x{area.Height}");
        OnCaptureStatusChanged?.Invoke($"Захват области ({_targetFps} FPS)");
    }

    public void StartWindowCapture(IntPtr windowHandle, Action<ImageSource> onFrameCaptured, int fps = 30)
    {
        if (_isDisposed) return;
        
        _onFrameCaptured = onFrameCaptured;
        _targetFps = Math.Max(10, Math.Min(fps, 60));
        _isWindowCapture = true;
        _currentWindowHandle = windowHandle;
        _captureArea = null;
        
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        
        _captureTimer = new System.Timers.Timer(1000.0 / _targetFps);
        _captureTimer.Elapsed += CaptureFrame;
        _captureTimer.AutoReset = true;
        _captureTimer.Start();
        
        Debug.WriteLine($"🎬 Захват окна запущен с FPS: {_targetFps}, Handle: {windowHandle}");
        OnCaptureStatusChanged?.Invoke($"Захват окна ({_targetFps} FPS)");
    }

    public void StopCapture()
    {
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        _captureTimer = null;
        _onFrameCaptured = null;
        _isWindowCapture = false;
        _currentWindowHandle = IntPtr.Zero;
        _captureArea = null;
        
        // Очищаем кэш
        _cachedBitmapSource = null;
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        
        Debug.WriteLine("🛑 Захват остановлен");
        OnCaptureStatusChanged?.Invoke("Захват остановлен");
    }

    private void CaptureFrame(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_isDisposed || _onFrameCaptured == null) 
            return;

        if (_isCapturing) 
            return;

        _isCapturing = true;
        
        try
        {
            ImageSource frame;
            if (_isWindowCapture && _currentWindowHandle != IntPtr.Zero)
            {
                frame = _windowCaptureService.CaptureWindow(_currentWindowHandle);
            }
            else if (_captureArea.HasValue)
            {
                frame = CaptureArea(_captureArea.Value);
            }
            else
            {
                frame = CaptureScreen();
            }
            
            if (frame != null)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!_isDisposed && _onFrameCaptured != null)
                    {
                        _onFrameCaptured(frame);
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка захвата: {ex.Message}");
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private BitmapSource CaptureScreen()
    {
        try
        {
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            
            // Оптимизация: используем кэшированные объекты
            if (_cachedBitmap == null || _cachedBitmap.Width != screenBounds.Width || _cachedBitmap.Height != screenBounds.Height)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb);
            }

            using (var graphics = Graphics.FromImage(_cachedBitmap))
            {
                graphics.CopyFromScreen(screenBounds.X, screenBounds.Y, 0, 0, screenBounds.Size);
            }

            var bitmapSource = ConvertBitmapToBitmapSource(_cachedBitmap);
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка при захвате экрана: {ex.Message}");
            return CreateErrorImage("Ошибка захвата экрана");
        }
    }

    private BitmapSource CaptureArea(System.Drawing.Rectangle area)
    {
        try
        {
            // Учитываем DPI экрана
            var dpiX = 96.0;
            var dpiY = 96.0;
        
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = graphics.DpiX;
                dpiY = graphics.DpiY;
            }

            // Масштабируем координаты согласно DPI
            double scaleX = dpiX / 96.0;
            double scaleY = dpiY / 96.0;
        
            int scaledX = (int)(area.X * scaleX);
            int scaledY = (int)(area.Y * scaleY);
            int scaledWidth = (int)(area.Width * scaleX);
            int scaledHeight = (int)(area.Height * scaleY);

            if (scaledWidth <= 0 || scaledHeight <= 0)
            {
                Debug.WriteLine("❌ Ошибка: Нулевые размеры области захвата.");
                return CreateErrorImage("Ошибка: Нулевые размеры области");
            }

            // Оптимизация: используем кэшированные объекты
            if (_cachedBitmap == null || _cachedBitmap.Width != scaledWidth || _cachedBitmap.Height != scaledHeight)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
            }

            using (var graphics = Graphics.FromImage(_cachedBitmap))
            {
                graphics.CopyFromScreen(scaledX, scaledY, 0, 0, 
                    new System.Drawing.Size(scaledWidth, scaledHeight), 
                    CopyPixelOperation.SourceCopy);
            }

            var bitmapSource = ConvertBitmapToBitmapSource(_cachedBitmap);
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка при захвате области: {ex.Message}");
            return CreateErrorImage("Ошибка захвата области");
        }
    }

    public List<ModernWindowCaptureService.WindowInfo> GetAvailableWindows()
    {
        return _windowCaptureService.GetAvailableWindows();
    }

    public string GetWindowTitle(IntPtr hWnd)
    {
        return _windowCaptureService.GetAvailableWindows()
            .FirstOrDefault(w => w.Handle == hWnd)?.Title ?? "Unknown Window";
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

    private BitmapSource CreateErrorImage(string message)
    {
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            context.DrawRectangle(System.Windows.Media.Brushes.DarkRed, null, new Rect(0, 0, 800, 450));
            
            var errorText = new FormattedText(
                message,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                12,
                System.Windows.Media.Brushes.White,
                1.0);
            
            context.DrawText(errorText, new Point(20, 200));
        }
        
        var bitmap = new RenderTargetBitmap(800, 450, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            StopCapture();
            _windowCaptureService?.Dispose();
            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
            Debug.WriteLine("🗑️ ScreenCaptureService disposed");
        }
    }
}