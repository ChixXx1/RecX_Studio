using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace RecX_Studio.Models;

public enum SourceType
{
    ScreenCapture,    // Захват экрана
    WindowCapture,    // Захват окна
    AudioInput,       // Аудиовход
    AudioOutput,      // Аудиовыход
    Webcam,           // Веб-камера (оставляем один вариант)
    Image,            // Изображение
    Text,
    AreaCapture       // Захват области
}

public class MediaSource : INotifyPropertyChanged
{
    private string _name;
    private SourceType _type;
    private bool _isEnabled;
    private bool _isSelected;
    private IntPtr _windowHandle;
    
    // --- НОВОЕ ПОЛЕ И СВОЙСТВО ---
    private System.Drawing.Rectangle _captureArea;
    public System.Drawing.Rectangle CaptureArea
    {
        get => _captureArea;
        set
        {
            _captureArea = value;
            OnPropertyChanged();
        }
    }
    
    // --- НОВЫЕ ПОЛЯ ДЛЯ ВЕБ-КАМЕРЫ ---
    private int _webcamIndex = -1;
    public int WebcamIndex
    {
        get => _webcamIndex;
        set
        {
            _webcamIndex = value;
            OnPropertyChanged();
        }
    }

    private System.Drawing.Rectangle _webcamPosition = new System.Drawing.Rectangle(10, 10, 320, 240);
    public System.Drawing.Rectangle WebcamPosition
    {
        get => _webcamPosition;
        set
        {
            _webcamPosition = value;
            OnPropertyChanged();
        }
    }
    // --------------------------------

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    public SourceType Type
    {
        get => _type;
        set
        {
            _type = value;
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

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public IntPtr WindowHandle
    {
        get => _windowHandle;
        set
        {
            _windowHandle = value;
            OnPropertyChanged();
        }
    }

    public MediaSource(string name, SourceType type)
    {
        Name = name;
        Type = type;
        IsEnabled = true;
        WindowHandle = IntPtr.Zero;
        CaptureArea = System.Drawing.Rectangle.Empty;
        WebcamPosition = new System.Drawing.Rectangle(10, 10, 320, 240);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}