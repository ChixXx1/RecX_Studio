using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;

namespace RecX_Studio.Models;

public class Settings : INotifyPropertyChanged
{
    private string _recordingPath;
    private int _fps = 60;
    private string _videoFormat = "MP4";
    private bool _askForPathEachTime = false;
    private bool _recordAudio = true;
    private string _audioInputDevice = "";
    private string _audioOutputDevice = "";
    private int _audioBitrate = 128;

    public string RecordingPath
    {
        get => _recordingPath;
        set { _recordingPath = value; OnPropertyChanged(nameof(RecordingPath)); }
    }

    public int Fps
    {
        get => _fps;
        set { _fps = value; OnPropertyChanged(nameof(Fps)); }
    }

    public string VideoFormat
    {
        get => _videoFormat;
        set { _videoFormat = value; OnPropertyChanged(nameof(VideoFormat)); }
    }

    public bool AskForPathEachTime
    {
        get => _askForPathEachTime;
        set { _askForPathEachTime = value; OnPropertyChanged(nameof(AskForPathEachTime)); }
    }

    // Аудио настройки
    public bool RecordAudio
    {
        get => _recordAudio;
        set { _recordAudio = value; OnPropertyChanged(nameof(RecordAudio)); }
    }

    public string AudioInputDevice
    {
        get => _audioInputDevice;
        set { _audioInputDevice = value; OnPropertyChanged(nameof(AudioInputDevice)); }
    }

    public string AudioOutputDevice
    {
        get => _audioOutputDevice;
        set { _audioOutputDevice = value; OnPropertyChanged(nameof(AudioOutputDevice)); }
    }

    public int AudioBitrate
    {
        get => _audioBitrate;
        set { _audioBitrate = value; OnPropertyChanged(nameof(AudioBitrate)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Методы для сохранения и загрузки
    public void Save()
    {
        try
        {
            string settingsPath = GetSettingsPath();
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(settingsPath, json);
            Debug.WriteLine($"✅ Настройки сохранены: {settingsPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка сохранения настроек: {ex.Message}");
        }
    }

    public static Settings Load()
    {
        try
        {
            string settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                var settings = JsonConvert.DeserializeObject<Settings>(json);
                Debug.WriteLine($"✅ Настройки загружены: {settingsPath}");
                return settings ?? new Settings();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка загрузки настроек: {ex.Message}");
        }

        // Возвращаем настройки по умолчанию
        var defaultSettings = new Settings();
        defaultSettings.RecordingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "RecX Studio");
        return defaultSettings;
    }

    private static string GetSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "RecX Studio");
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.json");
    }
}