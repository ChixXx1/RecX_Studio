using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using RecX_Studio.Models;
using RecX_Studio.Services;

namespace RecX_Studio.Services
{
    public class RecordingService : IDisposable
    {
        private volatile bool _isRecording = false;
        private Process _ffmpegProcess;
        private Settings _settings;
        private StringBuilder _errorOutput;
        public string LastRecordingPath { get; private set; }

        // --- НОВЫЕ ПОЛЯ ДЛЯ ПАУЗЫ ---
        private List<string> _segmentFiles = new List<string>();
        private bool _isPaused = false;
        private string _tempDirectory;
        // --------------------------------

        // --- НОВОЕ ПОЛЕ ДЛЯ ВЕБ-КАМЕРЫ ---
        private MediaSource _currentSource;
        // --------------------------------

        public RecordingService(Settings settings)
        {
            _settings = settings;
            _errorOutput = new StringBuilder();
        }

        public void UpdateSettings(Settings newSettings)
        {
            _settings = newSettings;
            Debug.WriteLine("🔄 RecordingService обновлен с новыми настройками");
        }

        public void StartRecording(string outputPath, MediaSource source)
        {
            // --- ИЗМЕНЕНО: Обработка возобновления ---
            if (_isRecording && !_isPaused)
                throw new InvalidOperationException("Запись уже идет");

            if (_isPaused)
            {
                ResumeRecording();
                return;
            }
            // -----------------------------------------

            // --- НОВОЕ: Сохраняем источник для возобновления ---
            _currentSource = source;
            // -----------------------------------------

            Debug.WriteLine($"🎬 Попытка начать запись: {outputPath}");

            string ffmpegPath = GetFFmpegPath();
            if (!File.Exists(ffmpegPath))
                throw new FileNotFoundException("FFmpeg не найден");

            // --- НОВОЕ: Создание временной директории ---
            _tempDirectory = Path.Combine(Path.GetTempPath(), "RecX_Studio", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _segmentFiles.Clear();
            // -----------------------------------------

            string directory = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // --- НОВОЕ: Запись идет в сегмент ---
            string segmentPath = Path.Combine(_tempDirectory, $"segment_{_segmentFiles.Count}.mp4");
            _segmentFiles.Add(segmentPath);
            // -----------------------------------------

            string ffmpegArgs = BuildFFmpegArgs(segmentPath, source);
            Debug.WriteLine($"🔧 Команда FFmpeg: {ffmpegPath} {ffmpegArgs}");

            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            _ffmpegProcess = new Process { StartInfo = processInfo };
            _errorOutput.Clear();

            _ffmpegProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"FFmpeg: {e.Data}");
                    _errorOutput.AppendLine(e.Data);
                    
                    if (e.Data.Contains("frame=") && e.Data.Contains("time="))
                    {
                        Debug.WriteLine($"📹 Прогресс: {e.Data}");
                    }
                }
            };

            try
            {
                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();

                Thread.Sleep(3000);

                if (_ffmpegProcess.HasExited)
                {
                    string errorDetails = _errorOutput.ToString();
                    
                    if (errorDetails.Contains("Error opening input file"))
                    {
                        throw new Exception($"Не удалось открыть аудиоустройство. Убедитесь, что устройство не используется другой программой.\nДетали: {errorDetails}");
                    }
                    else if (errorDetails.Contains("I/O error"))
                    {
                        throw new Exception($"Ошибка ввода-вывода аудиоустройства. Проверьте подключение и права доступа.\nДетали: {errorDetails}");
                    }
                    else
                    {
                        throw new Exception($"FFmpeg завершился с ошибкой. Код: {_ffmpegProcess.ExitCode}\nДетали:\n{errorDetails}");
                    }
                }

                _isRecording = true;
                _isPaused = false; // Сбрасываем флаг паузы
                LastRecordingPath = outputPath;
                Debug.WriteLine($"✅ Запись начата: {segmentPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка запуска FFmpeg: {ex.Message}");
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
                throw new Exception($"Ошибка запуска записи: {ex.Message}");
            }
        }

        // --- НОВЫЙ МЕТОД: Пауза ---
        public void PauseRecording()
        {
            if (!_isRecording || _isPaused)
                return;
                
            Debug.WriteLine("⏸️ Пауза записи...");
            
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    _ffmpegProcess.StandardInput.WriteLine("q");
                    _ffmpegProcess.StandardInput.Flush();
                    
                    if (!_ffmpegProcess.WaitForExit(3000))
                    {
                        _ffmpegProcess.Kill();
                        _ffmpegProcess.WaitForExit(1000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Ошибка при остановке FFmpeg: {ex.Message}");
                }
            }
            
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            
            _isPaused = true;
            Debug.WriteLine("✅ Запись поставлена на паузу");
        }
        // ---------------------------------

        // --- ИЗМЕНЕННЫЙ МЕТОД: Возобновление ---
        public void ResumeRecording()
        {
            if (!_isRecording || !_isPaused)
                return;
                
            Debug.WriteLine("▶️ Возобновление записи...");
            
            string segmentPath = Path.Combine(_tempDirectory, $"segment_{_segmentFiles.Count}.mp4");
            _segmentFiles.Add(segmentPath);
            
            // Используем сохраненный источник
            if (_currentSource == null)
            {
                throw new Exception("Источник для возобновления записи не найден");
            }
            
            string ffmpegPath = GetFFmpegPath();
            string ffmpegArgs = BuildFFmpegArgs(segmentPath, _currentSource);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            _ffmpegProcess = new Process { StartInfo = processInfo };
            _errorOutput.Clear();

            _ffmpegProcess.ErrorDataReceived += (s, e) => { /* ... обработка ошибок ... */ };

            try
            {
                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                
                _isPaused = false;
                Debug.WriteLine($"✅ Запись возобновлена: {segmentPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка возобновления записи: {ex.Message}");
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
                throw new Exception($"Ошибка возобновления записи: {ex.Message}");
            }
        }
        // ---------------------------------

        public void StopRecording()
        {
            if (!_isRecording)
            {
                Debug.WriteLine("ℹ️ Запись уже остановлена");
                return;
            }

            try
            {
                Debug.WriteLine("🛑 Остановка записи...");

                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    try
                    {
                        _ffmpegProcess.StandardInput.WriteLine("q");
                        _ffmpegProcess.StandardInput.Flush();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Не удалось отправить команду остановки: {ex.Message}");
                    }

                    if (!_ffmpegProcess.WaitForExit(5000))
                    {
                        Debug.WriteLine("⏰ Таймаут, принудительная остановка...");
                        _ffmpegProcess.Kill();
                        _ffmpegProcess.WaitForExit(1000);
                    }
                    else
                    {
                        Debug.WriteLine("✅ FFmpeg завершился корректно");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Ошибка при остановке: {ex.Message}");
            }
            finally
            {
                _isRecording = false;
                _isPaused = false;
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;

                // --- НОВОЕ: Объединение сегментов ---
                if (_segmentFiles.Count > 0)
                {
                    MergeSegments(LastRecordingPath);
                }
                // ---------------------------------
            }
        }

        // --- НОВЫЙ МЕТОД: Объединение сегментов ---
        private void MergeSegments(string outputPath)
        {
            if (_segmentFiles.Count <= 1)
            {
                if (_segmentFiles.Count == 1 && File.Exists(_segmentFiles[0]))
                {
                    File.Move(_segmentFiles[0], outputPath, true);
                }
                return;
            }
            
            try
            {
                Debug.WriteLine($"🔧 Объединение {_segmentFiles.Count} сегментов в {outputPath}");
                
                string listPath = Path.Combine(_tempDirectory, "filelist.txt");
                using (var writer = new StreamWriter(listPath))
                {
                    foreach (var segment in _segmentFiles)
                    {
                        if (File.Exists(segment))
                        {
                            writer.WriteLine($"file '{segment}'");
                        }
                    }
                }
                
                string ffmpegPath = GetFFmpegPath();
                string arguments = $"-f concat -safe 0 -i \"{listPath}\" -c copy \"{outputPath}\"";
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                
                using (var process = Process.Start(processInfo))
                {
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Ошибка объединения сегментов: {error}");
                    }
                }
                
                Debug.WriteLine($"✅ Сегменты успешно объединены в {outputPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка объединения сегментов: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Не удалось удалить временную директорию: {ex.Message}");
                }
            }
        }
        // -----------------------------------------

        private string BuildFFmpegArgs(string outputPath, MediaSource source)
        {
            var args = new List<string>();

            // --- 1. ВХОДНЫЕ ПОТОКИ ---
            
            // Видеовход (всегда первый поток)
            string videoInputArgs = GetVideoInputArgs(source);
            args.Add(videoInputArgs);

            // --- 2. АУДИО ВХОДЫ ---
            bool hasSystemAudio = !string.IsNullOrEmpty(_settings.AudioOutputDevice) && IsValidAudioDevice(_settings.AudioOutputDevice);
            bool hasMicrophone = !string.IsNullOrEmpty(_settings.AudioInputDevice) && IsValidAudioDevice(_settings.AudioInputDevice);
            bool audioEnabled = _settings.RecordAudio && (hasSystemAudio || hasMicrophone);

            if (audioEnabled)
            {
                if (hasSystemAudio)
                {
                    string systemAudioArgs = GetAudioInputArgs(_settings.AudioOutputDevice, "Системный звук");
                    args.Add(systemAudioArgs);
                }

                if (hasMicrophone)
                {
                    string microphoneArgs = GetAudioInputArgs(_settings.AudioInputDevice, "Микрофон");
                    args.Add(microphoneArgs);
                }
            }

            // --- 3. МАППИНГ ПОТОКОВ ---
            
            args.Add("-map");
            args.Add("0:v");

            if (audioEnabled)
            {
                if (hasSystemAudio && hasMicrophone)
                {
                    args.Add("-filter_complex");
                    args.Add("\"[1:a][2:a]amix=inputs=2:duration=first[aout]\"");
                    args.Add("-map");
                    args.Add("\"[aout]\"");
                }
                else
                {
                    args.Add("-map");
                    args.Add("1:a");
                }
            }

            // --- 4. КОДЕКИ И НАСТРОЙКИ ---

            args.Add("-c:v libx264");
            args.Add("-preset veryfast");
            args.Add("-tune zerolatency");
            args.Add($"-r {_settings.Fps}");
            args.Add("-pix_fmt yuv420p");
            args.Add("-crf 23");
            args.Add("-maxrate 6M");
            args.Add("-bufsize 12M");
            args.Add("-g 60");
            args.Add("-profile:v high");
            args.Add("-level 4.2");
            args.Add("-threads 0");
            args.Add("-movflags +faststart");

            if (audioEnabled)
            {
                args.Add("-c:a aac");
                args.Add($"-b:a {_settings.AudioBitrate}k");
                args.Add("-ar 48000");
                args.Add("-ac 2");
            }

            args.Add($"\"{outputPath}\"");

            return string.Join(" ", args);
        }

        private string GetVideoInputArgs(MediaSource source)
        {
            if (source.Type == SourceType.Webcam && source.WebcamIndex >= 0) // ДОБАВЛЕНО
            {
                var webcamService = new WebcamCaptureService();
                var webcams = webcamService.GetAvailableWebcams();
                var webcam = webcams.FirstOrDefault(w => w.Index == source.WebcamIndex);
                
                if (webcam != null)
                {
                    return $"-f dshow -framerate {_settings.Fps} -i video=\"{webcam.Name}\"";
                }
                else
                {
                    throw new ArgumentException($"Веб-камера с индексом {source.WebcamIndex} не найдена");
                }
            }
            else if (source.Type == SourceType.WindowCapture && source.WindowHandle != IntPtr.Zero)
            {
                var windowService = new ModernWindowCaptureService();
                var windows = windowService.GetAvailableWindows();
                var windowInfo = windows.FirstOrDefault(w => w.Handle == source.WindowHandle);
                string windowTitle = windowInfo?.Title ?? "Unknown";
                windowTitle = windowTitle.Replace("\"", "\\\"");
                
                return $"-f gdigrab -framerate {_settings.Fps} -draw_mouse 1 -i title=\"{windowTitle}\"";
            }
            else if (source.Type == SourceType.AreaCapture && source.CaptureArea != Rectangle.Empty)
            {
                var area = source.CaptureArea;
                
                if (area.Width <= 0 || area.Height <= 0)
                {
                    Debug.WriteLine("❌ Ошибка: Область захвата имеет нулевые размеры.");
                    throw new ArgumentException("Область захвата имеет недопустимые размеры.");
                }

                int evenWidth = RoundToEven(area.Width);
                int evenHeight = RoundToEven(area.Height);
                
                Debug.WriteLine($"📏 Захват области: {area.Width}x{area.Height} -> {evenWidth}x{evenHeight}");
                
                return $"-f gdigrab -framerate {_settings.Fps} -draw_mouse 1 -offset_x {area.X} -offset_y {area.Y} -video_size {evenWidth}x{evenHeight} -i desktop";
            }
            else
            {
                return $"-f gdigrab -framerate {_settings.Fps} -draw_mouse 1 -i desktop";
            }
        }

        private string GetAudioInputArgs(string deviceName, string deviceType)
        {
            try
            {
                Debug.WriteLine($"🎵 Настройка аудиоустройства {deviceType}: {deviceName}");
                return $"-f dshow -i audio=\"{deviceName}\"";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка настройки аудиоустройства {deviceType}: {ex.Message}");
                throw new Exception($"Не удалось настроить аудиоустройство {deviceType}: {deviceName}");
            }
        }

        private int RoundToEven(int number)
        {
            return (number % 2 == 0) ? number : number - 1;
        }

        private bool IsValidAudioDevice(string deviceName)
        {
            return !string.IsNullOrEmpty(deviceName) && 
                   deviceName != "Не выбрано" &&
                   !deviceName.Contains("RecX_Studio.Models.AudioDeviceInfo");
        }

        private string GetFFmpegPath()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(currentDir, "ffmpeg.exe");
            
            if (File.Exists(ffmpegPath))
            {
                return ffmpegPath;
            }

            throw new FileNotFoundException($"FFmpeg не найден по пути: {ffmpegPath}");
        }

        private void CheckRecordingResult()
        {
            if (File.Exists(LastRecordingPath))
            {
                var fileInfo = new FileInfo(LastRecordingPath);
                Debug.WriteLine($"✅ Файл создан: {LastRecordingPath} ({fileInfo.Length} байт)");
                
                if (fileInfo.Length == 0)
                {
                    Debug.WriteLine("⚠️ Файл создан, но имеет нулевой размер");
                    try { File.Delete(LastRecordingPath); } catch { }
                }
            }
            else
            {
                Debug.WriteLine($"❌ Файл не создан: {LastRecordingPath}");
            }
        }

        public void Dispose()
        {
            StopRecording();
        }
    }
}