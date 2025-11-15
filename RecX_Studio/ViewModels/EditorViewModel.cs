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
            set => SetProperty(ref _startTimeText, value);
        }

        private string _endTimeText = "00:00:00";
        public string EndTimeText
        {
            get => _endTimeText;
            set => SetProperty(ref _endTimeText, value);
        }

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
            SaveCommand = new RelayCommand(Save);

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
                _player.Source = new Uri(VideoPath);
                _player.Play(); // Начинаем воспроизведение при загрузке
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
                Reset(); // Устанавливаем начальные значения
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
            _player.Position = TimeSpan.Zero; // Возвращаем в начало
            
            _isPlaying = false;
            _isPreviewMode = false;
            _previewTimer.Stop(); // Важно остановить таймер предпросмотра
            
            Debug.WriteLine("EditorViewModel: Воспроизведение полностью остановлено.");
        }

        private void SetStart()
        {
            if (TimeSpan.TryParse(StartTimeText, out TimeSpan result))
            {
                StartTime = result;
                _previewIsSet = true;
                if (StartTime > EndTime)
                {
                    EndTime = Duration;
                    EndTimeText = EndTimeFormatted;
                }
                // Если воспроизведение идет, останавливаем его для ясности
                if(_isPlaying) Stop(); 
                Debug.WriteLine($"EditorViewModel: Установлено начало: {StartTime}. Предпросмотр доступен.");
            }
            else
            {
                MessageBox.Show("Неверный формат времени для начала. Используйте формат ЧЧ:ММ:СС.", "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetEnd()
        {
            if (TimeSpan.TryParse(EndTimeText, out TimeSpan result))
            {
                EndTime = result;
                _previewIsSet = true;
                if (EndTime < StartTime)
                {
                    StartTime = TimeSpan.Zero;
                    StartTimeText = StartTimeFormatted;
                }
                // Если воспроизведение идет, останавливаем его для ясности
                if(_isPlaying) Stop();
                Debug.WriteLine($"EditorViewModel: Установлен конец: {EndTime}. Предпросмотр доступен.");
            }
            else
            {
                MessageBox.Show("Неверный формат времени для конца. Используйте формат ЧЧ:ММ:СС.", "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Reset()
        {
            StartTime = TimeSpan.Zero;
            EndTime = Duration;
            StartTimeText = StartTimeFormatted;
            EndTimeText = EndTimeFormatted;
            _previewIsSet = false; // Сбрасываем флаг доступности предпросмотра
            Debug.WriteLine("EditorViewModel: Границы сброшены. Предпросмотр недоступен.");
        }

        private async void Save()
        {
            if (StartTime >= EndTime)
            {
                MessageBox.Show("Начало отрезка не может быть позже или равно концу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        }

        private string FormatTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss");
        }
    }
}