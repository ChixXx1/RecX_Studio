using RecX_Studio.Models;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Diagnostics;
using RecX_Studio.Utils;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace RecX_Studio.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private Settings _settings;
        private Settings _originalSettings;
        private ObservableCollection<AudioDeviceInfo> _availableInputDevices;
        private ObservableCollection<AudioDeviceInfo> _availableOutputDevices;

        public Settings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public ICommand OKCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand BrowsePathCommand { get; }

        public string[] AvailableVideoFormats => new[]
        {
            "MP4",
            "MKV", 
            "AVI",
            "MOV",
            "WMV"
        };

        public int[] AvailableFps => new[]
        {
            15, 24, 25, 30, 40, 48, 50, 60, 75, 90, 120, 144, 165
        };

        public int[] AvailableAudioBitrates => new[] { 64, 96, 128, 192, 256, 320 };

        public ObservableCollection<AudioDeviceInfo> AvailableInputDevices
        {
            get => _availableInputDevices;
            set => SetProperty(ref _availableInputDevices, value);
        }

        public ObservableCollection<AudioDeviceInfo> AvailableOutputDevices
        {
            get => _availableOutputDevices;
            set => SetProperty(ref _availableOutputDevices, value);
        }

        public SettingsViewModel(Settings settings)
        {
            _settings = settings;
            _originalSettings = new Settings();
            CopySettings(settings, _originalSettings);

            // Инициализируем списки аудиоустройств
            InitializeAudioDevices();

            OKCommand = new RelayCommand(OK);
            CancelCommand = new RelayCommand(Cancel);
            ApplyCommand = new RelayCommand(Apply, CanApply);
            BrowsePathCommand = new RelayCommand(BrowsePath);
            
            Settings.PropertyChanged += (s, e) => ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
        }

        private void InitializeAudioDevices()
        {
            _availableInputDevices = new ObservableCollection<AudioDeviceInfo>();
            _availableOutputDevices = new ObservableCollection<AudioDeviceInfo>();

            // Добавляем опцию "Не выбрано"
            var noDevice = new AudioDeviceInfo { Name = "Не выбрано", Id = "" };
            _availableInputDevices.Add(noDevice);
            _availableOutputDevices.Add(noDevice);

            // Получаем список аудиоустройств через FFmpeg
            GetAudioDevicesFromFFmpeg();

            OnPropertyChanged(nameof(AvailableInputDevices));
            OnPropertyChanged(nameof(AvailableOutputDevices));
        }

        // ИЗМЕНЕНО: Улучшен метод для более точного определения типа устройства
        private void GetAudioDevicesFromFFmpeg()
        {
            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    Debug.WriteLine("❌ FFmpeg не найден для получения списка устройств");
                    return;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(processInfo))
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit(3000);

                    Debug.WriteLine("🔍 Поиск аудиоустройств...");

                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("[dshow") && line.Contains("\""))
                        {
                            int start = line.IndexOf('"') + 1;
                            int end = line.LastIndexOf('"');
                            if (start > 0 && end > start)
                            {
                                string deviceName = line.Substring(start, end - start);
                                string deviceType = line.Contains("(video)") ? "video" : 
                                                  line.Contains("(audio)") ? "audio" : "none";

                                if (deviceType == "audio")
                                {
                                    var deviceInfo = new AudioDeviceInfo 
                                    { 
                                        Name = deviceName, // Сохраняем точное имя
                                        Id = deviceName
                                    };

                                    // ИЗМЕНЕНО: Расширен список ключевых слов для определения типа
                                    string lowerName = deviceName.ToLower();
                                    if (lowerName.Contains("microphone") || lowerName.Contains("mic") || 
                                        lowerName.Contains("микрофон"))
                                    {
                                        _availableInputDevices.Add(deviceInfo);
                                        Debug.WriteLine($"🎤 Найден микрофон: {deviceName}");
                                    }
                                    else if (lowerName.Contains("stereo mix") || lowerName.Contains("what u hear") ||
                                             lowerName.Contains("virtual") || lowerName.Contains("стерео микшер"))
                                    {
                                        _availableOutputDevices.Add(deviceInfo);
                                        Debug.WriteLine($"🔊 Найден системный звук: {deviceName}");
                                    }
                                    else
                                    {
                                        // Если тип неясен, добавляем в оба списка
                                        _availableInputDevices.Add(deviceInfo);
                                        _availableOutputDevices.Add(deviceInfo);
                                        Debug.WriteLine($"⚠️ Аудиоустройство с неопределенным типом: {deviceName}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка получения списка аудиоустройств: {ex.Message}");
            }
        }

        private void BrowsePath()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку для сохранения записей",
                SelectedPath = string.IsNullOrEmpty(Settings.RecordingPath) 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) 
                    : Settings.RecordingPath,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.RecordingPath = dialog.SelectedPath;
            }
        }

        private void OK()
        {
            Apply();
            CloseWindow();
        }

        private void Cancel()
        {
            CopySettings(_originalSettings, Settings);
            CloseWindow();
        }

        private void Apply()
        {
            // Сохраняем настройки в файл
            Settings.Save();
            CopySettings(Settings, _originalSettings);
            ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
            
            Debug.WriteLine("✅ Настройки применены и сохранены:");
            Debug.WriteLine($"   📁 Путь: {Settings.RecordingPath}");
            Debug.WriteLine($"   🎞️ FPS: {Settings.Fps}");
            Debug.WriteLine($"   📹 Формат: {Settings.VideoFormat}");
            Debug.WriteLine($"   🔊 Аудио: {Settings.RecordAudio}");
            Debug.WriteLine($"   🎤 Микрофон: {Settings.AudioInputDevice}");
            Debug.WriteLine($"   🔊 Системный звук: {Settings.AudioOutputDevice}");
            Debug.WriteLine($"   🎵 Битрейт: {Settings.AudioBitrate} kbps");
            Debug.WriteLine($"   ❓ Спрашивать путь: {Settings.AskForPathEachTime}");
        }

        private bool CanApply()
        {
            return !SettingsEquals(Settings, _originalSettings);
        }

        private void CloseWindow()
        {
            System.Windows.Application.Current.Windows
                .OfType<Views.SettingsWindow>()
                .FirstOrDefault()?
                .Close();
        }

        private void CopySettings(Settings source, Settings target)
        {
            target.RecordingPath = source.RecordingPath;
            target.Fps = source.Fps;
            target.VideoFormat = source.VideoFormat;
            target.AskForPathEachTime = source.AskForPathEachTime;
            target.RecordAudio = source.RecordAudio;
            target.AudioInputDevice = source.AudioInputDevice;
            target.AudioOutputDevice = source.AudioOutputDevice;
            target.AudioBitrate = source.AudioBitrate;
        }

        private bool SettingsEquals(Settings a, Settings b)
        {
            return a.RecordingPath == b.RecordingPath &&
                   a.Fps == b.Fps &&
                   a.VideoFormat == b.VideoFormat &&
                   a.AskForPathEachTime == b.AskForPathEachTime &&
                   a.RecordAudio == b.RecordAudio &&
                   a.AudioInputDevice == b.AudioInputDevice &&
                   a.AudioOutputDevice == b.AudioOutputDevice &&
                   a.AudioBitrate == b.AudioBitrate;
        }
    }
}