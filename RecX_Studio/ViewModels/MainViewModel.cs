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
using System.Windows.Input;
using RecX_Studio.Utils;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using System.Threading.Tasks;

namespace RecX_Studio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ObservableCollection<MediaSource> _sources = new();
    private readonly StatusInfo _statusInfo = new();
    private readonly Timer _statusTimer;
    private readonly PerformanceCounter? _cpuCounter;
    
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly ScreenCaptureService _previewCaptureService;
    private readonly RecordingService _recordingService;
    private readonly DirectXCaptureService _directXCaptureService;
    
    private RecordingState _currentState = RecordingState.Idle;
    private TimeSpan _recordingTime = TimeSpan.Zero;
    private MediaSource _selectedSource;
    private ImageSource _previewImage;
    private bool _isScreenCaptureActive;
    private Settings _settings;

    // --- СВОЙСТВА ДЛЯ КНОПОК И ИХ СОСТОЯНИЙ ---
    private string _recordButtonText = "⏺ Начать запись";
    private Brush _recordButtonColor = Brushes.Red;
    private string _recordButtonIcon = "⏺";
    
    // --- УДАЛЕНО: Свойство SelectedFormat и массив SupportedFormats ---
    // Теперь формат всегда берется из Settings.VideoFormat
    
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

    public string RecordButtonIcon
    {
        get => _recordButtonIcon;
        set => SetProperty(ref _recordButtonIcon, value);
    }

    public MediaSource ActiveSource => _selectedSource ?? _sources.FirstOrDefault();

    // Для подсчета реального FPS
    private int _frameCount = 0;
    private DateTime _lastFpsUpdate = DateTime.Now;

    private bool _useDirectXCapture = false;

    public ObservableCollection<MediaSource> Sources => _sources;
    public StatusInfo StatusInfo => _statusInfo;
    
    public RecordingState CurrentState
    {
        get => _currentState;
        private set 
        { 
            if (SetProperty(ref _currentState, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    
    public TimeSpan RecordingTime => _recordingTime;
    
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
    
    // --- КОМАНДЫ ДЛЯ УПРАВЛЕНИЯ ЗАПИСЬЮ ---
    public RelayCommand StartRecordingCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand ChooseSaveLocationCommand { get; }
    // -----------------------------------------

    public MainViewModel()
    {
        _settings = Settings.Load();
        
        _screenCaptureService = new ScreenCaptureService();
        _previewCaptureService = new ScreenCaptureService();
        _recordingService = new RecordingService(_settings);
        _directXCaptureService = new DirectXCaptureService();
        
        StartRecordingCommand = new RelayCommand(StartRecording, () => CurrentState == RecordingState.Idle);
        PauseResumeCommand = new RelayCommand(PauseResumeRecording, () => CurrentState == RecordingState.Recording || CurrentState == RecordingState.Paused);
        StopRecordingCommand = new RelayCommand(StopRecording, () => CurrentState != RecordingState.Idle);
        ChooseSaveLocationCommand = new RelayCommand(ChooseSaveLocation);
        
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

    // --- ИЗМЕНЕННЫЙ МЕТОД: Активация захвата для предпросмотра в режиме ожидания ---
    private void ActivateActiveSource()
    {
        var activeSource = ActiveSource;
        if (activeSource == null || !activeSource.IsEnabled)
        {
            StopScreenCapture();
            Debug.WriteLine("🛑 Активный источник не найден или неактивен, захват остановлен.");
            return;
        }

        Debug.WriteLine($"🎯 Активация источника для предпросмотра (Idle): {activeSource.Name} ({activeSource.Type})");

        try
        {
            _previewCaptureService.StopCapture();
            StopScreenCapture();

            switch (activeSource.Type)
            {
                case SourceType.ScreenCapture:
                    StartScreenCapture();
                    break;
                case SourceType.WindowCapture:
                    if (activeSource.WindowHandle != IntPtr.Zero)
                    {
                        StartWindowCapture(activeSource.WindowHandle);
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ WindowHandle для активного источника окна равен IntPtr.Zero.");
                    }
                    break;
                case SourceType.AreaCapture:
                    if (activeSource.CaptureArea != Rectangle.Empty)
                    {
                        StartAreaCapture(activeSource.CaptureArea);
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ CaptureArea для активного источника области пуста.");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка активации источника {activeSource.Name}: {ex.Message}");
        }
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
                // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Перезагружаем настройки после их сохранения ---
                Debug.WriteLine("✅ Настройки сохранены через OK, перезагружаем в MainViewModel.");
                _settings = Settings.Load(); 
                OnPropertyChanged(nameof(Settings)); // Уведомляем UI
                _recordingService.UpdateSettings(Settings);
            }
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
            }

            ActivateActiveSource();

            OnPropertyChanged(nameof(SelectedSource));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            OnPropertyChanged(nameof(ActiveSource));
        }
    }
    
    public bool CanMoveUp => SelectedSource != null && _sources.IndexOf(SelectedSource) > 0;
    public bool CanMoveDown => SelectedSource != null && _sources.IndexOf(SelectedSource) < _sources.Count - 1;

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
        
        _screenCaptureService.StartWindowCapture(windowHandle, OnFrameCaptured, Settings.Fps);
        _isScreenCaptureActive = true;
        _useDirectXCapture = false;
        Debug.WriteLine($"✅ Захват окна запущен: {windowHandle}");
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

    private void CaptureDirectXFrame()
    {
        if (!_isScreenCaptureActive || !_useDirectXCapture) return;

        try
        {
            var frame = _directXCaptureService.CaptureScreen();
            if (frame != null)
            {
                OnFrameCaptured(frame);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка DirectX захвата: {ex.Message}");
            _useDirectXCapture = false;
            StartScreenCapture();
        }
    }

    public void StartRecording()
    {
        if (ActiveSource == null)
        {
            ShowNoSourceWarning();
            return;
        }

        try
        {
            // --- ИЗМЕНЕНО: Используем формат из настроек ---
            string fileName = $"record_{DateTime.Now:yyyyMMdd_HHmmss}.{Settings.VideoFormat.ToLower()}";
            string outputPath = Path.Combine(GetRecordingDirectory(), fileName);

            Debug.WriteLine($"🎬 Начало записи: {outputPath}");
            
            _recordingService.StartRecording(outputPath, ActiveSource);
            CurrentState = RecordingState.Recording;
            _recordingTime = TimeSpan.Zero;
            
            _frameCount = 0;
            _lastFpsUpdate = DateTime.Now;
        
            StartDedicatedPreviewCapture();
        
            UpdateRecordButtonStyle();
            StatusInfo.RecordingTime = "00:00:00";
            StatusInfo.Fps = "00.00";
        
            OnPropertyChanged(nameof(CurrentState));
        
            Debug.WriteLine($"🎬 Запись начата: {ActiveSource.Name}, Формат: {Settings.VideoFormat}, FPS: {Settings.Fps}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка начала записи: {ex.Message}");
            ShowErrorMessage($"Ошибка начала записи: {ex.Message}");
        
            CurrentState = RecordingState.Idle;
            OnPropertyChanged(nameof(CurrentState));
        }
    }

    private void StartDedicatedPreviewCapture()
    {
        var activeSource = ActiveSource;
        if (activeSource == null) return;

        Debug.WriteLine($"🎯 Запуск предпросмотра во время записи: {activeSource.Name} ({activeSource.Type})");
        try
        {
            switch (activeSource.Type)
            {
                case SourceType.ScreenCapture:
                    _previewCaptureService.StartCapture(OnFrameCaptured, Settings.Fps);
                    break;
                case SourceType.WindowCapture:
                    if (activeSource.WindowHandle != IntPtr.Zero)
                    {
                        _previewCaptureService.StartWindowCapture(activeSource.WindowHandle, OnFrameCaptured, Settings.Fps);
                    }
                    break;
                case SourceType.AreaCapture:
                    if (activeSource.CaptureArea != Rectangle.Empty)
                    {
                        _previewCaptureService.StartAreaCapture(activeSource.CaptureArea, OnFrameCaptured, Settings.Fps);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка запуска предпросмотра во время записи: {ex.Message}");
        }
    }

    public void PauseRecording()
    {
        if (CurrentState != RecordingState.Recording) return;

        try
        {
            _recordingService.PauseRecording();
            CurrentState = RecordingState.Paused;
            
            UpdateRecordButtonStyle();
            
            OnPropertyChanged(nameof(CurrentState));
            Debug.WriteLine("✅ Запись поставлена на паузу");
            StatusInfo.RecordingTime += " (Пауза)";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка паузы записи: {ex.Message}");
            ShowErrorMessage($"Ошибка паузы записи: {ex.Message}");
        }
    }

    public void ResumeRecording()
    {
        if (CurrentState != RecordingState.Paused) return;
        
        try
        {
            _recordingService.ResumeRecording();
            CurrentState = RecordingState.Recording;
            
            UpdateRecordButtonStyle();
            
            OnPropertyChanged(nameof(CurrentState));
            Debug.WriteLine("✅ Запись возобновлена");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка возобновления записи: {ex.Message}");
            ShowErrorMessage($"Ошибка возобновления записи: {ex.Message}");
        }
    }

    public void PauseResumeRecording()
    {
        if (CurrentState == RecordingState.Recording)
        {
            PauseRecording();
        }
        else if (CurrentState == RecordingState.Paused)
        {
            ResumeRecording();
        }
    }

    public void StopRecording()
    {
        try
        {
            Debug.WriteLine($"🔄 Остановка записи. Текущее состояние: {CurrentState}");

            _recordingService.StopRecording();
    
            _previewCaptureService.StopCapture();
    
            CurrentState = RecordingState.Idle;
        
            StatusInfo.RecordingTime = "00:00:00";
            _recordingTime = TimeSpan.Zero;
        
            UpdateRecordButtonStyle();
            StatusInfo.Fps = "00.00";
    
            OnPropertyChanged(nameof(CurrentState));
    
            Debug.WriteLine("✅ UI обновлен, запись остановлена");

            ActivateActiveSource();

            if (File.Exists(_recordingService.LastRecordingPath))
            {
                var fileInfo = new FileInfo(_recordingService.LastRecordingPath);
                ShowSuccessMessage($"Запись сохранена!\n\nФайл: {Path.GetFileName(_recordingService.LastRecordingPath)}\nРазмер: {FormatFileSize(fileInfo.Length)}\nПуть: {_recordingService.LastRecordingPath}");
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

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private void UpdateRecordButtonStyle()
    {
        if (CurrentState == RecordingState.Recording)
        {
            RecordButtonText = "⏸ Пауза";
            RecordButtonColor = Brushes.Cyan;
            RecordButtonIcon = "⏸";
        }
        else if (CurrentState == RecordingState.Paused)
        {
            RecordButtonText = "▶ Возобновить";
            RecordButtonColor = Brushes.Orange;
            RecordButtonIcon = "▶";
        }
        else // Idle
        {
            RecordButtonText = "⏺ Начать запись";
            RecordButtonColor = Brushes.Red;
            RecordButtonIcon = "⏺";
        }
        
        OnPropertyChanged(nameof(RecordButtonText));
        OnPropertyChanged(nameof(RecordButtonColor));
        OnPropertyChanged(nameof(RecordButtonIcon));
    }

    // --- ИЗМЕНЕННЫЙ МЕТОД: Теперь использует формат из настроек ---
    private void ChooseSaveLocation()
    {
        var saveDialog = new SaveFileDialog
        {
            // --- ИЗМЕНЕНО ---
            Filter = GetVideoFormatFilterFor(Settings.VideoFormat),
            DefaultExt = Settings.VideoFormat.ToLower(),
            FileName = $"record_{DateTime.Now:yyyyMMdd_HHmmss}.{Settings.VideoFormat.ToLower()}",
            InitialDirectory = GetRecordingDirectory()
        };

        if (saveDialog.ShowDialog() == true)
        {
            // Формат обновляется из выбранного расширения файла
            Settings.VideoFormat = Path.GetExtension(saveDialog.FileName).TrimStart('.').ToUpper();
            OnPropertyChanged(nameof(Settings)); // Обновляем UI, если он привязан к Settings.VideoFormat
            UpdateStatus($"Место сохранения установлено: {Path.GetFileName(saveDialog.FileName)}");
        }
    }

    private string GetRecordingDirectory()
    {
        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "RecX Studio");
        
        if (!Directory.Exists(defaultDir))
        {
            Directory.CreateDirectory(defaultDir);
        }
        
        return defaultDir;
    }

    // --- НОВЫЙ ВСПОМОГАТЕЛЬНЫЙ МЕТОД ---
    private string GetVideoFormatFilterFor(string currentFormat)
    {
        // Создаем фильтр, где текущий формат идет первым
        var formats = new[] { "MP4", "MKV", "AVI", "MOV", "WebM", "WMV" };
        var currentFormatLower = currentFormat.ToLower();
        
        string primaryFilter = $"{currentFormat} files (*.{currentFormatLower})|*.{currentFormatLower}";
        string otherFilters = string.Join("|", formats.Where(f => f != currentFormat)
                                                       .Select(f => $"{f} files (*.{f.ToLower()})|*.{f.ToLower()}"));
        
        return $"{primaryFilter}|{otherFilters}|All files (*.*)|*.*";
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
            var emptySource = new MediaSource("Пустой экран", SourceType.ScreenCapture);
            StartRecordingWithSource(emptySource);
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

    private void StartRecordingWithSource(MediaSource source)
    {
        try
        {
            // --- ИЗМЕНЕНО: Используем формат из настроек ---
            string fileName = $"record_{DateTime.Now:yyyyMMdd_HHmmss}.{Settings.VideoFormat.ToLower()}";
            string outputPath = Path.Combine(GetRecordingDirectory(), fileName);

            _recordingService.StartRecording(outputPath, source);
            CurrentState = RecordingState.Recording;
            _recordingTime = TimeSpan.Zero;
            
            UpdateRecordButtonStyle();
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
        
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(ActiveSource));
        ActivateActiveSource();
    }

    public void RemoveSource(MediaSource source)
    {
        if (source == null)
        {
            Debug.WriteLine("Попытка удалить null источник");
            return;
        }

        // Если удаляемый источник в данный момент активен и захватывается,
        // то нужно остановить захват.
        if ((source.Type == SourceType.ScreenCapture || source.Type == SourceType.WindowCapture) && _isScreenCaptureActive)
        {
            // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Останавливаем захват немедленно ---
            StopScreenCapture();
        }

        int index = _sources.IndexOf(source);
        _sources.Remove(source);
        Debug.WriteLine($"Удален источник: {source.Name}. Всего источников: {_sources.Count}");
    
        // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Обновляем SelectedSource ДО вызова ActivateActiveSource ---
        // Если мы удаляли выбранный элемент, нужно выбрать новый.
        if (SelectedSource == source)
        {
            // Выбираем следующий доступный элемент или null, если список пуст
            SelectedSource = _sources.Any() ? _sources.FirstOrDefault() : null;
        }
    
        // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Всегда вызываем ActivateActiveSource после изменений ---
        // Это обеспечит, что если источников стало 0, предпросмотр будет сброшен.
        ActivateActiveSource();
    
        // Уведомляем UI об изменениях
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(ActiveSource));
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
                OnPropertyChanged(nameof(ActiveSource));
                ActivateActiveSource();
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
                OnPropertyChanged(nameof(ActiveSource));
                ActivateActiveSource();
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
        if (source == null) return;

        source.IsEnabled = !source.IsEnabled;
        ActivateActiveSource();
    }

    public void StartScreenCapture()
    {
        Debug.WriteLine($"🎬 Запрос на запуск захвата экрана с FPS: {Settings.Fps}");
        
        if (_useDirectXCapture && _directXCaptureService.IsAvailable())
        {
            Debug.WriteLine("✅ Используем DirectX захват для лучшей производительности");
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

    private void StopFrameCapture()
    {
        StopScreenCapture();
    }

    public void StopScreenCapture()
    {
        if (_isScreenCaptureActive)
        {
            Debug.WriteLine("🛑 Запрос на остановку захвата...");
            
            _screenCaptureService.StopCapture();
            
            _directXCaptureTimer?.Stop();
            _directXCaptureTimer?.Dispose();
            _directXCaptureTimer = null;
            
            _isScreenCaptureActive = false;
            PreviewImage = CreateDefaultPreview();
            Debug.WriteLine("✅ Захват остановлен в MainViewModel");
        }
    }

    private void UpdateStatus(string message)
    {
        StatusInfo.RecordingTime = message;
    }

    private void OnFrameCaptured(ImageSource frame)
    {
        if (frame == null)
        {
            Debug.WriteLine("❌ Получен null кадр");
            return;
        }

        if (CurrentState == RecordingState.Recording)
        {
            if (frame is BitmapSource bitmapSource)
            {
                int stride = bitmapSource.PixelWidth * 3;
                byte[] pixels = new byte[bitmapSource.PixelHeight * stride];
                bitmapSource.CopyPixels(pixels, stride, 0);
            }
        }

        if (CurrentState == RecordingState.Recording)
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
                
                Debug.WriteLine($"📊 Real FPS: {actualFps}, Target: {Settings.Fps}");
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
        _previewCaptureService?.Dispose(); 
        _recordingService?.Dispose();
        _directXCaptureService?.Dispose();
        _directXCaptureTimer?.Stop();
        _directXCaptureTimer?.Dispose();
        _cpuCounter?.Dispose();
    }
}