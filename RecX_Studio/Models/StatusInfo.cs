using System.ComponentModel;

namespace RecX_Studio.Models;

public class StatusInfo : INotifyPropertyChanged
{
    private string _currentTime = "00:00:00";
    private string _recordingTime = "00:00:00";
    private string _cpuUsage = "0%";
    private string _fps = "00.00";

    public string CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            OnPropertyChanged(nameof(CurrentTime));
        }
    }

    public string RecordingTime
    {
        get => _recordingTime;
        set
        {
            _recordingTime = value;
            OnPropertyChanged(nameof(RecordingTime));
        }
    }

    public string CpuUsage
    {
        get => _cpuUsage;
        set
        {
            _cpuUsage = value;
            OnPropertyChanged(nameof(CpuUsage));
        }
    }

    public string Fps
    {
        get => _fps;
        set
        {
            _fps = value;
            OnPropertyChanged(nameof(Fps));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}