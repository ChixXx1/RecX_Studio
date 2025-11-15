using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RecX_Studio.Models;

public class AudioDevice : INotifyPropertyChanged
{
    private string _name;
    private string _id;
    private bool _isEnabled;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    public string Id
    {
        get => _id;
        set
        {
            _id = value;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public AudioDevice(string name, string id)
    {
        Name = name;
        Id = id;
        IsEnabled = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}