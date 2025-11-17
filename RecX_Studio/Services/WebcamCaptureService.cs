using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace RecX_Studio.Services
{
    public class WebcamCaptureService : IDisposable
    {
        private readonly List<WebcamDeviceInfo> _availableDevices = new List<WebcamDeviceInfo>();
        private bool _isInitialized = false;
        
        public class WebcamDeviceInfo
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public int Index { get; set; }
        }

        public WebcamCaptureService()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Получаем список доступных веб-камер через FFmpeg
                GetWebcamDevicesFromFFmpeg();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка инициализации сервиса веб-камеры: {ex.Message}");
            }
        }

        private void GetWebcamDevicesFromFFmpeg()
        {
            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    Debug.WriteLine("❌ FFmpeg не найден для получения списка веб-камер");
                    return;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit(3000);

                    Debug.WriteLine("🔍 Поиск веб-камер...");

                    var lines = output.Split('\n');
                    int videoIndex = 0;
                    
                    foreach (var line in lines)
                    {
                        if (line.Contains("[dshow") && line.Contains("\""))
                        {
                            int start = line.IndexOf('"') + 1;
                            int end = line.LastIndexOf('"');
                            if (start > 0 && end > start)
                            {
                                string deviceName = line.Substring(start, end - start);
                                
                                if (line.Contains("(video)"))
                                {
                                    var deviceInfo = new WebcamDeviceInfo 
                                    { 
                                        Name = deviceName,
                                        Id = deviceName,
                                        Index = videoIndex++
                                    };
                                    
                                    _availableDevices.Add(deviceInfo);
                                    Debug.WriteLine($"📷 Найдена веб-камера: {deviceName}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка получения списка веб-камер: {ex.Message}");
            }
        }

        public List<WebcamDeviceInfo> GetAvailableWebcams()
        {
            return _availableDevices;
        }

        public BitmapSource CaptureWebcamFrame(int deviceIndex)
        {
            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    Debug.WriteLine("❌ FFmpeg не найден для захвата кадра с веб-камеры");
                    return CreateErrorImage("FFmpeg не найден");
                }

                var deviceInfo = _availableDevices.FirstOrDefault(d => d.Index == deviceIndex);
                if (deviceInfo == null)
                {
                    Debug.WriteLine($"❌ Устройство с индексом {deviceIndex} не найдено");
                    return CreateErrorImage($"Устройство не найдено");
                }

                // Временный файл для захвата кадра
                string tempImagePath = Path.Combine(Path.GetTempPath(), $"webcam_frame_{Guid.NewGuid()}.jpg");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-f dshow -i video=\"{deviceInfo.Name}\" -vframes 1 -y \"{tempImagePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit(3000);
                    
                    if (File.Exists(tempImagePath))
                    {
                        using (var bitmap = new Bitmap(tempImagePath))
                        {
                            var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                            bitmapSource.Freeze();
                            
                            // Удаляем временный файл
                            try { File.Delete(tempImagePath); } catch { }
                            
                            return bitmapSource;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("❌ Не удалось захватить кадр с веб-камеры");
                        return CreateErrorImage("Не удалось захватить кадр");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка захвата кадра с веб-камеры: {ex.Message}");
                return CreateErrorImage($"Ошибка: {ex.Message}");
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
                    bitmapData.Width, bitmapData.Height,
                    96, 96,
                    System.Windows.Media.PixelFormats.Bgr24,
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

        private BitmapSource CreateErrorImage(string message)
        {
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                context.DrawRectangle(Brushes.DarkRed, null, new Rect(0, 0, 320, 240));
                
                var errorText = new FormattedText(
                    message,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    12,
                    Brushes.White,
                    1.0);
                
                context.DrawText(errorText, new Point(20, 100));
            }
            
            var bitmap = new RenderTargetBitmap(320, 240, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);
            bitmap.Freeze();
            return bitmap;
        }

        public void Dispose()
        {
            Debug.WriteLine("✅ WebcamCaptureService disposed");
        }
    }
}