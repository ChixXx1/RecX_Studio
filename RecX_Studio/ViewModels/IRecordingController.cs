using System;

namespace RecX_Studio.ViewModels;

public interface IRecordingController
{
    event EventHandler? RecordingStarted;
    event EventHandler? RecordingPaused;
    event EventHandler? RecordingResumed;
    event EventHandler? RecordingStopped;
    
    void StartRecording();
    void PauseRecording();
    void ResumeRecording();
    void StopRecording();
}