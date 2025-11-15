using RecX_Studio.Models;
using RecX_Studio.Utils;
using System;
using System.Windows.Input;

namespace RecX_Studio.ViewModels;

public class ControlPanelViewModel : ObservableObject, IRecordingController
{
    private RecordingState _currentState = RecordingState.Idle;
    private TimeSpan _recordingTime = TimeSpan.Zero;
    private Timer? _recordingTimer;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingPaused;
    public event EventHandler? RecordingResumed;
    public event EventHandler? RecordingStopped;

    public RecordingState CurrentState
    {
        get => _currentState;
        private set 
        { 
            SetProperty(ref _currentState, value);
            // Уведомляем об изменении состояния для обновления команд
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public TimeSpan RecordingTime
    {
        get => _recordingTime;
        private set => SetProperty(ref _recordingTime, value);
    }

    public string TimerText => RecordingTime.ToString(@"hh\:mm\:ss");

    // Свойства для текста и иконки кнопок
    public string RecordButtonText => CurrentState switch
    {
        RecordingState.Recording => "⏹ Остановить",
        RecordingState.Paused => "▶ Возобновить",
        _ => "⏺ Начать запись"
    };

    public string PauseButtonText => CurrentState == RecordingState.Recording ? "⏸ Пауза" : "▶ Возобновить";

    // Команды
    public RelayCommand StartRecordingCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopRecordingCommand { get; }

    public ControlPanelViewModel()
    {
        StartRecordingCommand = new RelayCommand(StartRecording, () => CurrentState == RecordingState.Idle);
        PauseResumeCommand = new RelayCommand(PauseResumeRecording, () => CurrentState == RecordingState.Recording || CurrentState == RecordingState.Paused);
        StopRecordingCommand = new RelayCommand(StopRecording, () => CurrentState != RecordingState.Idle);
    }

    public void StartRecording()
    {
        CurrentState = RecordingState.Recording;
        StartTimer();
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    public void PauseRecording()
    {
        if (CurrentState == RecordingState.Recording)
        {
            CurrentState = RecordingState.Paused;
            StopTimer();
            RecordingPaused?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ResumeRecording()
    {
        if (CurrentState == RecordingState.Paused)
        {
            CurrentState = RecordingState.Recording;
            StartTimer();
            RecordingResumed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopRecording()
    {
        CurrentState = RecordingState.Idle;
        StopTimer();
        RecordingTime = TimeSpan.Zero;
        RecordingStopped?.Invoke(this, EventArgs.Empty);
    }

    private void PauseResumeRecording()
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

    private void StartTimer()
    {
        _recordingTimer = new Timer(_ =>
        {
            if (CurrentState == RecordingState.Recording)
            {
                RecordingTime = RecordingTime.Add(TimeSpan.FromSeconds(1));
            }
        }, null, 0, 1000);
    }

    private void StopTimer()
    {
        _recordingTimer?.Dispose();
        _recordingTimer = null;
    }
}