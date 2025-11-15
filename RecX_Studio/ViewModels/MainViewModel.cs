using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.IO;
using Microsoft.Win32;
using Timer = System.Timers.Timer;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;
using RecX_Studio.Models;
using RecX_Studio.Services;
using RecX_Studio.Views;
using System.Collections.Generic;
using System.Drawing;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace RecX_Studio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ObservableCollection<MediaSource> _sources = new();
    private readonly StatusInfo _statusInfo = new();
    private readonly Timer _statusTimer;
    private readonly PerformanceCounter? _cpuCounter;
    
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly RecordingService _recordingService;
    private readonly DirectXCaptureService _directXCaptureService; // НОВОЕ: DirectX захват
    
    private RecordingState _currentState = RecordingState.Idle;
    private TimeSpan _recordingTime = TimeSpan.Zero;
    private MediaSource _selectedSource;
    private ImageSource _previewImage;
    private bool _isScreenCaptureActive;
    private string _recordButtonText = "Начать запись";
    private Brush _recordButtonColor = Brushes.Red;
    
    private Settings _settings;

    // Для подсчета реального FPS
    private int _frameCount = 0;
    private DateTime _lastFpsUpdate = DateTime.Now;

    // НОВОЕ: Для переключения между методами захвата
    private bool _useDirectXCapture = false;

    public ObservableCollection<MediaSource> Sources => _sources;
    public StatusInfo StatusInfo => _statusInfo;
    public RecordingState CurrentState => _currentState;
    public TimeSpan RecordingTime => _recordingTime;
    
    public string RecordButtonText
    {
        get => _recordButtonText;
        set => SetProperty(ref _recordButtonText, value);
    }
    
    public Brush RecordButtonColor
    {
        get => _recordButtonColor;
        set => SetProperty(ref _recordButtonColor, value);
    }
    
    public ImageSource PreviewImage
    {
        get => _previewImage;
        set => SetProperty(ref _previewImage, value);
    }
    
    public Settings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }
    
    public void StartAreaSelection(Action<Rectangle> onAreaSelected)
    {
        _screenCaptureService.StartAreaSelection(onAreaSelected);
    }

    public void StartAreaCapture(Rectangle area)
    {
        Debug.WriteLine($"🎬 Запуск захвата области: {area.Width}x{area.Height} at ({area.X}, {area.Y})");

        try
        {
            StopScreenCapture();
            
            // Для захвата области используем стандартный метод
            _screenCaptureService.StartAreaCapture(area, OnFrameCaptured, Settings.Fps);
            _isScreenCaptureActive = true;
            _useDirectXCapture = false;
        
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var areaPreview = CreateAreaPreviewImage(area);
                PreviewImage = areaPreview;
            });
        
            Debug.WriteLine($"✅ Захват области запущен: {area.Width}x{area.Height}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка запуска захвата области: {ex.Message}");
            ShowErrorMessage($"Ошибка запуска захвата области: {ex.Message}");
        }
    }
    
    private ImageSource CreateAreaPreviewImage(Rectangle area)
    {
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(area.Width, area.Height, 
                       System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.X, area.Y, 0, 0, 
                    new System.Drawing.Size(area.Width, area.Height), 
                    System.Drawing.CopyPixelOperation.SourceCopy);

                return ConvertBitmapToBitmapSource(bitmap);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка создания превью области: {ex.Message}");
            return CreateAreaPlaceholderImage(area);
        }
    }

    private BitmapSource ConvertBitmapToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            bitmap.PixelFormat);

        try
        {
            return BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                96, 96,
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
    
    private ImageSource CreateAreaPlaceholderImage(Rectangle area)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(40, 40, 40)), 
                null, 
                new Rect(0, 0, area.Width, area.Height));
        
            drawingContext.DrawRectangle(
                Brushes.Red,
                new Pen(Brushes.Red, 2),
                new Rect(0, 0, area.Width, area.Height));
        
            var infoText = new FormattedText(
                $"Область захвата\n{area.Width} × {area.Height}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                14,
                Brushes.White,
                1.0);
        
            drawingContext.DrawText(infoText, new Point(10, area.Height / 2 - 20));
        }
    
        var bitmap = new RenderTargetBitmap(
            Math.Max(area.Width, 1), 
            Math.Max(area.Height, 1), 
            96, 96, 
            PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
    
        return bitmap;
    }

    public void OpenSettings()
    {
        try
        {
            var settingsViewModel = new SettingsViewModel(Settings);
            var settingsWindow = new SettingsWindow(settingsViewModel);
            var result = settingsWindow.ShowDialog();
            
            if (result == true)
            {
                Debug.WriteLine("✅ Настройки сохранены через OK");
            }
            
            _recordingService.UpdateSettings(Settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка открытия настроек: {ex.Message}");
            ShowErrorMessage($"Ошибка открытия настроек: {ex.Message}");
        }
    }
    
    public void OpenEditor()
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Выберите видеофайл для редактирования",
            Filter = "Video Files (*.mp4;*.mkv;*.avi)|*.mp4;*.mkv;*.avi|All files (*.*)|*.*"
        };

        if (openDialog.ShowDialog() == true)
        {
            try
            {
                EditorWindow editorWindow = new EditorWindow(openDialog.FileName);
                editorWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть редактор: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    public MediaSource SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (_selectedSource != null)
            {
                _selectedSource.IsSelected = false;
            }
    
            _selectedSource = value;
        
            if (_selectedSource != null)
            {
                _selectedSource.IsSelected = true;
            
                if (_selectedSource.IsEnabled)
                {
                    if (_selectedSource.Type == SourceType.ScreenCapture)
                    {
                        StartScreenCapture();
                    }
                    else if (_selectedSource.Type == SourceType.WindowCapture && _selectedSource.WindowHandle != IntPtr.Zero)
                    {
                        StartWindowCapture(_selectedSource.WindowHandle);
                    }
                    else if (_selectedSource.Type == SourceType.AreaCapture && _selectedSource.CaptureArea != Rectangle.Empty)
                    {
                        StartAreaCapture(_selectedSource.CaptureArea);
                    }
                }
                else
                {
                    StopScreenCapture();
                }
            }
            else
            {
                StopScreenCapture();
            }
    
            OnPropertyChanged(nameof(SelectedSource));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }
    }
    
    public bool CanMoveUp => SelectedSource != null && _sources.IndexOf(SelectedSource) > 0;
    public bool CanMoveDown => SelectedSource != null && _sources.IndexOf(SelectedSource) < _sources.Count - 1;

    public MainViewModel()
    {
        _settings = Settings.Load();
        
        _screenCaptureService = new ScreenCaptureService();
        _recordingService = new RecordingService(_settings);
        _directXCaptureService = new DirectXCaptureService(); // НОВОЕ: Инициализация DirectX захвата
        
        // Подписка на событие изменения статуса захвата
        _screenCaptureService.OnCaptureStatusChanged += (message) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"Статус захвата изменен: {message}");
                StatusInfo.RecordingTime = message; 
            });
        };

        _statusTimer = new Timer(1000);
        _statusTimer.Elapsed += UpdateStatusInfo;
        _statusTimer.Start();
        
        CheckFFmpegOnStartup();

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }
        catch
        {
            _cpuCounter = null;
        }

        PreviewImage = CreateDefaultPreview();

        UpdateStatusInfo(null, null);
        
        Debug.WriteLine("🎯 MainViewModel инициализирован");
        
        // НОВОЕ: Проверяем доступность DirectX захвата
        if (_directXCaptureService.IsAvailable())
        {
            Debug.WriteLine("✅ DirectX захват доступен");
            _useDirectXCapture = true;
        }
        else
        {
            Debug.WriteLine("⚠️ DirectX захват недоступен, используем стандартный метод");
            _useDirectXCapture = false;
        }
    }

    public List<ModernWindowCaptureService.WindowInfo> GetAvailableWindows()
    {
        var windows = _screenCaptureService.GetAvailableWindows();
        Debug.WriteLine($"📋 Найдено окон: {windows.Count}");
        foreach (var window in windows)
        {
            Debug.WriteLine($"   - {window.Title} [{window.Handle}]");
        }
        return windows;
    }

    public void AddWindowSource(IntPtr windowHandle, string windowTitle)
    {
        var source = new MediaSource($"Окно: {windowTitle}", SourceType.WindowCapture)
        {
            IsEnabled = true,
            WindowHandle = windowHandle
        };

        AddSource(source);
    }

    public void StartWindowCapture(IntPtr windowHandle)
    {
        Debug.WriteLine($"🎬 Запуск захвата окна: {windowHandle}");
        
        try
        {
            StopScreenCapture();
            
            _screenCaptureService.StartWindowCapture(windowHandle, OnFrameCaptured, Settings.Fps);
            _isScreenCaptureActive = true;
            _useDirectXCapture = false; // Для захвата окна используем стандартный метод
            Debug.WriteLine($"✅ Захват окна запущен: {windowHandle}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка запуска захвата окна: {ex.Message}");
            ShowErrorMessage($"Ошибка запуска захвата окна: {ex.Message}");
        }
    }
    
    private void CheckFFmpegOnStartup()
    {
        try
        {
            Debug.WriteLine("🔍 Проверка FFmpeg...");

            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(currentDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                Debug.WriteLine("❌ ffmpeg.exe не найден в папке приложения");
                ShowErrorMessage($"FFmpeg не найден!\n\nФайл ffmpeg.exe должен находиться в папке:\n{currentDirectory}\n\nСкачайте FFmpeg с https://ffmpeg.org/download.html и поместите в эту папку.");
                return;
            }

            Debug.WriteLine("✅ ffmpeg.exe найден, проверяем работоспособность...");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();
                
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && output.Contains("ffmpeg version"))
                {
                    Debug.WriteLine("✅ FFmpeg работает корректно");
                }
                else
                {
                    Debug.WriteLine($"❌ FFmpeg не работает. Код выхода: {process.ExitCode}");
                    ShowErrorMessage("FFmpeg найден, но не работает корректно. Возможно файл поврежден.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка проверки FFmpeg: {ex.Message}");
            ShowErrorMessage($"Ошибка проверки FFmpeg: {ex.Message}");
        }
    }

    private void UpdateStatusInfo(object? sender, ElapsedEventArgs? e)
    {
        StatusInfo.CurrentTime = DateTime.Now.ToString("HH:mm:ss");

        if (_currentState == RecordingState.Recording)
        {
            _recordingTime = _recordingTime.Add(TimeSpan.FromSeconds(1));
            StatusInfo.RecordingTime = _recordingTime.ToString(@"hh\:mm\:ss");
        }

        if (_cpuCounter != null)
        {
            try
            {
                var cpuUsage = _cpuCounter.NextValue();
                StatusInfo.CpuUsage = $"{cpuUsage:00}%";
            }
            catch
            {
                StatusInfo.CpuUsage = "N/A";
            }
        }
        else
        {
            StatusInfo.CpuUsage = "N/A";
        }

        // FPS теперь обновляется в реальном времени в OnFrameCaptured
        // Здесь только сбрасываем значение если не записываем
        if (_currentState != RecordingState.Recording)
        {
            StatusInfo.Fps = "00.00";
        }
    }
    
    public void UpdateAreaPreview(Rectangle area)
    {
        if (_isScreenCaptureActive && SelectedSource?.Type == SourceType.AreaCapture)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var areaPreview = CreateAreaPreviewImage(area);
                PreviewImage = areaPreview;
            });
        }
    }

    // НОВЫЙ МЕТОД: Захват кадра с использованием DirectX
    private void CaptureDirectXFrame()
    {
        if (!_isScreenCaptureActive || !_useDirectXCapture) return;

        try
        {
            var frame = _directXCaptureService.CaptureScreen();
            if (frame != null)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!_isScreenCaptureActive) return;
                    
                    PreviewImage = frame;
                    
                    // Подсчет FPS для DirectX захвата
                    if (_currentState == RecordingState.Recording)
                    {
                        _frameCount++;
                        var now = DateTime.Now;
                        var elapsed = (now - _lastFpsUpdate).TotalSeconds;
                        
                        if (elapsed >= 1.0)
                        {
                            var actualFps = (int)(_frameCount / elapsed);
                            StatusInfo.Fps = $"{actualFps:00.00}";
                            _frameCount = 0;
                            _lastFpsUpdate = now;
                            
                            Debug.WriteLine($"📊 DirectX FPS: {actualFps}, Целевой: {Settings.Fps}");
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка DirectX захвата: {ex.Message}");
            // При ошибке переключаемся на стандартный захват
            _useDirectXCapture = false;
            StartScreenCapture();
        }
    }

    public void ToggleRecording()
    {
        Debug.WriteLine($"🔄 ToggleRecording. Текущее состояние: {_currentState}");

        if (_currentState == RecordingState.Recording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    public void StartRecording()
    {
        if (SelectedSource == null)
        {
            ShowNoSourceWarning();
            return;
        }

        if (!SelectedSource.IsEnabled)
        {
            ShowSourceDisabledWarning();
            return;
        }

        var savePath = GetRecordingSavePath();
        if (string.IsNullOrEmpty(savePath))
            return;

        try
        {
            Debug.WriteLine($"🎬 Начало записи с FPS: {Settings.Fps}");
            
            _recordingService.StartRecording(savePath, SelectedSource);
            _currentState = RecordingState.Recording;
            _recordingTime = TimeSpan.Zero;
            
            // Сбрасываем счетчики FPS
            _frameCount = 0;
            _lastFpsUpdate = DateTime.Now;
        
            RecordButtonText = "Остановить запись";
            RecordButtonColor = Brushes.Cyan;
            StatusInfo.RecordingTime = "00:00:00";
            StatusInfo.Fps = "00.00";
        
            OnPropertyChanged(nameof(CurrentState));
        
            Debug.WriteLine($"🎬 Запись начата: {SelectedSource.Name}, FPS: {Settings.Fps}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка начала записи: {ex.Message}");
            ShowErrorMessage($"Ошибка начала записи: {ex.Message}");
        
            _currentState = RecordingState.Idle;
            OnPropertyChanged(nameof(CurrentState));
        }
    }

    public void StopRecording()
    {
        try
        {
            Debug.WriteLine($"🔄 Остановка записи. Текущее состояние: {_currentState}");

            _recordingService.StopRecording();
        
            _currentState = RecordingState.Idle;
            RecordButtonText = "Начать запись";
            RecordButtonColor = Brushes.Red;
            StatusInfo.Fps = "00.00";
        
            OnPropertyChanged(nameof(CurrentState));
        
            Debug.WriteLine("✅ UI обновлен, запись остановлена");

            if (File.Exists(_recordingService.LastRecordingPath))
            {
                ShowSuccessMessage($"Запись сохранена: {_recordingService.LastRecordingPath}");
            }
            else
            {
                ShowErrorMessage("Запись не была сохранена. Файл не создан.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка остановки записи: {ex.Message}");
            ShowErrorMessage($"Ошибка остановки записи: {ex.Message}");
        }
    }

    private string GetRecordingSavePath()
    {
        if (Settings.AskForPathEachTime || string.IsNullOrEmpty(Settings.RecordingPath))
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = GetVideoFormatFilter(),
                FileName = GetDefaultFileName(),
                DefaultExt = GetDefaultExtension(),
                InitialDirectory = GetInitialDirectory()
            };

            return saveDialog.ShowDialog() == true ? saveDialog.FileName : null;
        }
        else
        {
            string directory = Settings.RecordingPath;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            string fileName = GetDefaultFileName();
            return Path.Combine(directory, fileName);
        }
    }

    private string GetVideoFormatFilter()
    {
        return Settings.VideoFormat switch
        {
            "MP4" => "MP4 файлы (*.mp4)|*.mp4",
            "MKV" => "MKV файлы (*.mkv)|*.mkv",
            "AVI" => "AVI файлы (*.avi)|*.avi",
            "MOV" => "MOV файлы (*.mov)|*.mov",
            "WMV" => "WMV файлы (*.wmv)|*.wmv",
            _ => "Все файлы (*.*)|*.*"
        };
    }

    private string GetDefaultFileName()
    {
        return $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.{GetDefaultExtension()}";
    }

    private string GetDefaultExtension()
    {
        return Settings.VideoFormat.ToLower();
    }

    private string GetInitialDirectory()
    {
        if (!string.IsNullOrEmpty(Settings.RecordingPath) && Directory.Exists(Settings.RecordingPath))
        {
            return Settings.RecordingPath;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    private void ShowNoSourceWarning()
    {
        var result = MessageBox.Show(
            "Нет источников\n\nПохоже, вы ещё не добавили ни одного источника. Вы будете выводить только пустой экран. Уверены, что хотите этого?\n\nВы можете добавить источники, нажав значок + под блоком «Источник» в главном окне в любое время.",
            "Предупреждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var savePath = GetRecordingSavePath();
            if (!string.IsNullOrEmpty(savePath))
            {
                var emptySource = new MediaSource("Пустой экран", SourceType.ScreenCapture);
                StartRecordingWithSource(emptySource, savePath);
            }
        }
    }

    private void ShowSourceDisabledWarning()
    {
        MessageBox.Show(
            "Выбранный источник отключен. Пожалуйста, включите источник перед началом записи.",
            "Источник отключен",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowErrorMessage(string message)
    {
        MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowSuccessMessage(string message)
    {
        MessageBox.Show(message, "Запись завершена", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void StartRecordingWithSource(MediaSource source, string savePath)
    {
        try
        {
            _recordingService.StartRecording(savePath, source);
            _currentState = RecordingState.Recording;
            _recordingTime = TimeSpan.Zero;
            
            RecordButtonText = "Остановить запись";
            RecordButtonColor = Brushes.Cyan;
            StatusInfo.RecordingTime = "00:00:00";
            
            OnPropertyChanged(nameof(CurrentState));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка начала записи: {ex.Message}");
            ShowErrorMessage($"Ошибка начала записи: {ex.Message}");
        }
    }

    public void AddSource(MediaSource source)
    {
        if (source == null)
        {
            Debug.WriteLine("Попытка добавить null источник");
            return;
        }

        _sources.Add(source);
        Debug.WriteLine($"Добавлен источник: {source.Name}. Всего источников: {_sources.Count}");
        
        SelectedSource = source;
        
        OnPropertyChanged(nameof(Sources));
    }

    public void RemoveSource(MediaSource source)
    {
        if (source == null)
        {
            Debug.WriteLine("Попытка удалить null источник");
            return;
        }

        if ((source.Type == SourceType.ScreenCapture || source.Type == SourceType.WindowCapture) && _isScreenCaptureActive)
        {
            StopScreenCapture();
        }

        _sources.Remove(source);
        Debug.WriteLine($"Удален источник: {source.Name}. Всего источников: {_sources.Count}");
        
        if (SelectedSource == source)
        {
            SelectedSource = null;
        }
        
        OnPropertyChanged(nameof(Sources));
    }

    public void MoveSourceUp()
    {
        if (SelectedSource != null)
        {
            var currentIndex = _sources.IndexOf(SelectedSource);
            if (currentIndex > 0)
            {
                _sources.Move(currentIndex, currentIndex - 1);
                OnPropertyChanged(nameof(Sources));
                OnPropertyChanged(nameof(CanMoveUp));
                OnPropertyChanged(nameof(CanMoveDown));
            }
        }
    }

    public void MoveSourceDown()
    {
        if (SelectedSource != null)
        {
            var currentIndex = _sources.IndexOf(SelectedSource);
            if (currentIndex < _sources.Count - 1)
            {
                _sources.Move(currentIndex, currentIndex + 1);
                OnPropertyChanged(nameof(Sources));
                OnPropertyChanged(nameof(CanMoveUp));
                OnPropertyChanged(nameof(CanMoveDown));
            }
        }
    }

    public void RemoveSelectedSource()
    {
        if (SelectedSource != null)
        {
            RemoveSource(SelectedSource);
        }
    }

    public void ToggleSource(MediaSource source)
    {
        if (source.Type == SourceType.ScreenCapture)
        {
            if (source.IsEnabled)
            {
                StartScreenCapture();
            }
            else
            {
                StopScreenCapture();
            }
        }
        else if (source.Type == SourceType.WindowCapture)
        {
            if (source.IsEnabled && source.WindowHandle != IntPtr.Zero)
            {
                StartWindowCapture(source.WindowHandle);
            }
            else
            {
                StopScreenCapture();
            }
        }
        else if (source.Type == SourceType.AreaCapture)
        {
            if (source.IsEnabled && source.CaptureArea != Rectangle.Empty)
            {
                StartAreaCapture(source.CaptureArea);
            }
            else
            {
                StopScreenCapture();
            }
        }
    }

    public void StartScreenCapture()
    {
        if (!_isScreenCaptureActive)
        {
            Debug.WriteLine($"🎬 Запрос на запуск захвата экрана с FPS: {Settings.Fps}");
            
            // НОВОЕ: Выбираем метод захвата в зависимости от доступности DirectX
            if (_useDirectXCapture && _directXCaptureService.IsAvailable())
            {
                Debug.WriteLine("✅ Используем DirectX захват для лучшей производительности");
                // Запускаем таймер для DirectX захвата
                StartDirectXCaptureTimer();
            }
            else
            {
                Debug.WriteLine("⚠️ Используем стандартный захват экрана");
                _screenCaptureService.StartCapture(OnFrameCaptured, Settings.Fps);
            }
            
            _isScreenCaptureActive = true;
            Debug.WriteLine("✅ Захват экрана запущен в MainViewModel");
        }
        else
        {
            Debug.WriteLine("⚠️ Захват экрана уже активен");
        }
    }

    // НОВЫЙ МЕТОД: Таймер для DirectX захвата
    private Timer _directXCaptureTimer;
    private void StartDirectXCaptureTimer()
    {
        _directXCaptureTimer?.Stop();
        _directXCaptureTimer?.Dispose();
        
        _directXCaptureTimer = new Timer(1000.0 / Settings.Fps);
        _directXCaptureTimer.Elapsed += (s, e) => CaptureDirectXFrame();
        _directXCaptureTimer.AutoReset = true;
        _directXCaptureTimer.Start();
    }

    public void StopScreenCapture()
    {
        if (_isScreenCaptureActive)
        {
            Debug.WriteLine("🛑 Запрос на остановку захвата...");
            
            // Останавливаем оба возможных метода захвата
            _screenCaptureService.StopCapture();
            
            _directXCaptureTimer?.Stop();
            _directXCaptureTimer?.Dispose();
            _directXCaptureTimer = null;
            
            _isScreenCaptureActive = false;
            PreviewImage = CreateDefaultPreview();
            Debug.WriteLine("✅ Захват остановлен в MainViewModel");
        }
    }

    private void OnFrameCaptured(ImageSource frame)
    {
        if (frame == null)
        {
            Debug.WriteLine("❌ Получен null кадр");
            return;
        }

        // Подсчет реального FPS только во время записи
        if (_currentState == RecordingState.Recording)
        {
            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            
            if (elapsed >= 1.0) // Обновляем FPS раз в секунду
            {
                // Показываем целевой FPS, так как фильтр FFmpeg его гарантирует
                // Но для отладки можно показать и реальный
                var actualFps = (int)(_frameCount / elapsed);
                StatusInfo.Fps = $"{Settings.Fps:00.00}"; // Показываем ЦЕЛЕВОЙ FPS
                _frameCount = 0;
                _lastFpsUpdate = now;
                
                Debug.WriteLine($"📊 Захват FPS: {actualFps}, Целевой (в файле): {Settings.Fps}");
            }
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                PreviewImage = frame;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка установки кадра: {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private ImageSource CreateDefaultPreview()
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(30, 30, 30)), 
                null, 
                new Rect(0, 0, 800, 450));
                
            var text = new FormattedText(
                "Область предпросмотра",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Arial"),
                16,
                Brushes.White,
                1.0);
            
            drawingContext.DrawText(text, new Point(250, 215));
        }
        
        var bitmap = new RenderTargetBitmap(800, 450, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        Settings?.Save();
        
        _statusTimer?.Stop();
        _statusTimer?.Dispose();
        _screenCaptureService?.Dispose();
        _recordingService?.Dispose();
        _directXCaptureService?.Dispose();
        _directXCaptureTimer?.Stop();
        _directXCaptureTimer?.Dispose();
        _cpuCounter?.Dispose();
    }
}