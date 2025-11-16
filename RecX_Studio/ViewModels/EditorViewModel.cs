using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RecX_Studio.Models;
using RecX_Studio.Services;
using RecX_Studio.Utils;

namespace RecX_Studio.ViewModels
{
    public class EditorViewModel : ObservableObject
    {
        private readonly MediaElement _player;
        private readonly EditingService _editingService;

        // ДВА ТАЙМЕРА: один для UI, другой для контроля предпросмотра
        private readonly DispatcherTimer _uiUpdateTimer;
        private readonly DispatcherTimer _previewTimer;

        public string VideoPath { get; }

        // ФЛАГИ для управления состоянием
        private bool _isPlaying = false;
        private bool _isPreviewMode = false; // Играем ли мы обрезанный фрагмент?
        private bool _previewIsSet = false;  // Установлены ли границы для предпросмотра?
        private bool _isInitialized = false; // Загружено ли видео в первый раз?

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set { SetProperty(ref _duration, value); OnPropertyChanged(nameof(DurationFormatted)); OnPropertyChanged(nameof(DurationTotalSeconds)); }
        }

        private TimeSpan _currentPosition;
        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            set
            {
                SetProperty(ref _currentPosition, value);
                OnPropertyChanged(nameof(CurrentTimeFormatted));
                OnPropertyChanged(nameof(CurrentPositionTotalSeconds));
            }
        }

        public double DurationTotalSeconds => Duration.TotalSeconds;
        public double CurrentPositionTotalSeconds => CurrentPosition.TotalSeconds;

        private TimeSpan _startTime;
        public TimeSpan StartTime
        {
            get => _startTime;
            set { SetProperty(ref _startTime, value); OnPropertyChanged(nameof(StartTimeFormatted)); }
        }

        private TimeSpan _endTime;
        public TimeSpan EndTime
        {
            get => _endTime;
            set { SetProperty(ref _endTime, value); OnPropertyChanged(nameof(EndTimeFormatted)); }
        }

        public string DurationFormatted => FormatTime(Duration);
        public string CurrentTimeFormatted => FormatTime(CurrentPosition);
        public string StartTimeFormatted => FormatTime(StartTime);
        public string EndTimeFormatted => FormatTime(EndTime);

        // Команды управления
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SetStartCommand { get; }
        public ICommand SetEndCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand SaveCommand { get; }

        // Свойства для ввода времени
        private string _startTimeText = "00:00:00";
        public string StartTimeText
        {
            get => _startTimeText;
            set
            {
                if (SetProperty(ref _startTimeText, value))
                {
                    ValidateTimes(); // Запускаем валидацию при изменении текста
                }
            }
        }

        private string _endTimeText = "00:00:00";
        public string EndTimeText
        {
            get => _endTimeText;
            set
            {
                if (SetProperty(ref _endTimeText, value))
                {
                    ValidateTimes(); // Запускаем валидацию при изменении текста
                }
            }
        }

        // --- НОВЫЕ: Свойства для хранения текста ошибок ---
        private string _startTimeError;
        public string StartTimeError
        {
            get => _startTimeError;
            set => SetProperty(ref _startTimeError, value);
        }

        private string _endTimeError;
        public string EndTimeError
        {
            get => _endTimeError;
            set => SetProperty(ref _endTimeError, value);
        }
        // -------------------------------------------------

        public EditorViewModel(string videoPath, MediaElement player)
        {
            Debug.WriteLine("EditorViewModel: Конструктор с улучшенной логикой вызван.");
            VideoPath = videoPath;
            _player = player;
            _editingService = new EditingService();

            if (!File.Exists(VideoPath))
            {
                MessageBox.Show($"Файл не найден: {VideoPath}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Инициализация команд
            PlayPauseCommand = new RelayCommand(PlayPause);
            StopCommand = new RelayCommand(Stop);
            SetStartCommand = new RelayCommand(SetStart);
            SetEndCommand = new RelayCommand(SetEnd);
            ResetCommand = new RelayCommand(Reset);
            // --- ИЗМЕНЕНО: Добавлен делегат CanSave ---
            SaveCommand = new RelayCommand(Save, CanSave);

            // Инициализация таймера для обновления UI (текущая позиция)
            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;

            // Инициализация таймера для контроля конца предпросмотра
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _previewTimer.Tick += PreviewTimer_Tick;

            // Подписываемся на события плеера
            _player.MediaOpened += Player_MediaOpened;
            _player.MediaEnded += Player_MediaEnded;
            _player.MediaFailed += Player_MediaFailed;

            try
            {
                // Загружаем видео, но НЕ начинаем воспроизведение автоматически
                _player.Source = new Uri(VideoPath);
                _player.Pause(); // Явно ставим на паузу после установки источника
                Debug.WriteLine("EditorViewModel: Источник видео установлен, воспроизведение на паузе.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить видео: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Таймер обновляет текущую позицию в UI
        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_player != null && _player.NaturalDuration.HasTimeSpan)
            {
                CurrentPosition = _player.Position;
            }
        }

        // Таймер следит, чтобы предпросмотр не зашел за границы
        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            if (_player.Position >= EndTime)
            {
                Debug.WriteLine("EditorViewModel: Предпросмотр достиг конца, останавливаем.");
                Stop(); // Останавливаем все, когда дошли до конца отрезка
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (_player.NaturalDuration.HasTimeSpan)
            {
                Duration = _player.NaturalDuration.TimeSpan;
                _uiUpdateTimer.Start();

                // Инициализируем границы только при первой загрузке
                if (!_isInitialized)
                {
                    Debug.WriteLine("EditorViewModel: Первичная инициализация границ отрезка.");
                    Reset();
                    _isInitialized = true;
                }
            }
        }

        // Срабатывает только при воспроизведении полного видео
        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Если мы не в режиме предпросмотра, значит, полное видео доиграло
            if (!_isPreviewMode)
            {
                Debug.WriteLine("EditorViewModel: Полное видео доиграло, останавливаем.");
                Stop();
            }
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MessageBox.Show($"Ошибка воспроизведения видео: {e.ErrorException.Message}", "Ошибка медиа", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ОСНОВНАЯ ЛОГИКА ВОСПРОИЗВЕДЕНИЯ
        private void PlayPause()
        {
            if (_isPlaying)
            {
                // Если что-то играет, ставим на паузу
                _player.Pause();
                _isPlaying = false;
                _previewTimer.Stop(); // Останавливаем таймер предпросмотра
                Debug.WriteLine("EditorViewModel: Видео поставлено на паузу.");
            }
            else
            {
                // Если ничего не играет, начинаем
                if (_previewIsSet)
                {
                    // Если границы установлены, включаем режим предпросмотра
                    Debug.WriteLine($"EditorViewModel: Запуск предпросмотра с {StartTime} до {EndTime}.");
                    _isPreviewMode = true;
                    _player.Position = StartTime; // Перематываем на начало
                    _previewTimer.Start(); // Запускаем таймер контроля
                }
                else
                {
                    // Иначе играем с текущей позиции
                    Debug.WriteLine("EditorViewModel: Запуск воспроизведения полного видео.");
                    _isPreviewMode = false;
                }

                _player.Play();
                _isPlaying = true;
            }
        }

        private void Stop()
        {
            _player.Stop();
            // Перематываем на начало отрезка, если он установлен, иначе в начало файла
            _player.Position = _previewIsSet ? StartTime : TimeSpan.Zero;

            _isPlaying = false;
            _isPreviewMode = false;
            _previewTimer.Stop(); // Важно остановить таймер предпросмотра

            Debug.WriteLine($"EditorViewModel: Воспроизведение остановлено. Позиция установлена на {_player.Position}.");
        }

        // --- НОВЫЙ: Централизованный метод валидации ---
        private void ValidateTimes()
        {
            // 1. Проверяем формат и диапазон для начала
            if (!TimeSpan.TryParse(StartTimeText, out TimeSpan startParsed))
            {
                StartTimeError = "Неверный формат. Используйте ЧЧ:ММ:СС.";
            }
            else if (startParsed < TimeSpan.Zero || startParsed > Duration)
            {
                StartTimeError = "Время выходит за пределы видео.";
            }
            else
            {
                StartTimeError = null; // Ошибок нет
            }

            // 2. Проверяем формат и диапазон для конца
            if (!TimeSpan.TryParse(EndTimeText, out TimeSpan endParsed))
            {
                EndTimeError = "Неверный формат. Используйте ЧЧ:ММ:СС.";
            }
            else if (endParsed < TimeSpan.Zero || endParsed > Duration)
            {
                EndTimeError = "Время выходит за пределы видео.";
            }
            else
            {
                EndTimeError = null; // Ошибок нет
            }

            // 3. Проверяем, что начало не позже конца (только если оба времени корректны)
            if (StartTimeError == null && EndTimeError == null && startParsed >= endParsed)
            {
                EndTimeError = "Конец должен быть позже начала.";
            }

            // Обновляем состояние команд
            CommandManager.InvalidateRequerySuggested();
        }
        // -------------------------------------------------

        // --- ИЗМЕНЕННЫЕ: Упрощенные методы, т.к. основная логика в ValidateTimes ---
        private void SetStart()
        {
            if (string.IsNullOrEmpty(StartTimeError) && TimeSpan.TryParse(StartTimeText, out var result))
            {
                StartTime = result;
                _previewIsSet = true;
                if (StartTime > EndTime)
                {
                    EndTime = Duration;
                    EndTimeText = EndTimeFormatted;
                }
                if (_isPlaying) Stop();
                Debug.WriteLine($"EditorViewModel: Установлено начало: {StartTime}. Предпросмотр доступен.");
            }
        }

        private void SetEnd()
        {
            if (string.IsNullOrEmpty(EndTimeError) && TimeSpan.TryParse(EndTimeText, out var result))
            {
                EndTime = result;
                _previewIsSet = true;
                if (EndTime < StartTime)
                {
                    StartTime = TimeSpan.Zero;
                    StartTimeText = StartTimeFormatted;
                }
                if (_isPlaying) Stop();
                Debug.WriteLine($"EditorViewModel: Установлен конец: {EndTime}. Предпросмотр доступен.");
            }
        }

        private void Reset()
        {
            StartTime = TimeSpan.Zero;
            EndTime = Duration;
            StartTimeText = StartTimeFormatted;
            EndTimeText = EndTimeFormatted;
            _previewIsSet = false;
            // --- НОВОЕ: Очищаем ошибки ---
            StartTimeError = null;
            EndTimeError = null;
            // ---------------------------------
            Debug.WriteLine("EditorViewModel: Границы сброшены. Предпросмотр недоступен.");
        }
        // -------------------------------------------------

        // --- НОВЫЙ: Метод для проверки возможности сохранения ---
        private bool CanSave()
        {
            return string.IsNullOrEmpty(StartTimeError) && string.IsNullOrEmpty(EndTimeError) && StartTime < EndTime;
        }
        // -------------------------------------------------

        private async void Save()
        {
            // Дополнительная проверка на всякий случай
            if (!CanSave())
            {
                MessageBox.Show("Исправьте ошибки в полях времени перед сохранением.", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Video Files (*.mp4;*.mkv)|*.mp4;*.mkv|All files (*.*)|*.*",
                Title = "Сохранить отрезок видео",
                FileName = Path.GetFileNameWithoutExtension(VideoPath) + "_trimmed" + Path.GetExtension(VideoPath)
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    MessageBox.Show("Начинаю экспорт. Это может занять время. Пожалуйста, подождите.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
                    await Task.Run(() => _editingService.TrimVideo(VideoPath, saveDialog.FileName, StartTime, EndTime));
                    MessageBox.Show($"Видео успешно сохранено в:\n{saveDialog.FileName}", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при сохранении видео: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Cleanup()
        {
            _uiUpdateTimer?.Stop();
            _previewTimer?.Stop();
            _player?.Stop();
            _player?.Close();
            Debug.WriteLine("EditorViewModel: Ресурсы освобождены.");
        }

        private string FormatTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss");
        }
    }
}