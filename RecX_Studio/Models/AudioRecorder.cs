namespace RecX_Studio.Models;

using NAudio.Wave;

public class AudioRecorder : IDisposable
{
    private WaveInEvent _microphone;
    private WasapiLoopbackCapture _systemAudio;

    public void StartRecording(AudioSource source, string outputPath)
    {
        if (source == AudioSource.Microphone)
        {
            _microphone = new WaveInEvent();
            _microphone.StartRecording();
        }
        else if (source == AudioSource.SystemAudio)
        {
            _systemAudio = new WasapiLoopbackCapture();
            _systemAudio.StartRecording();
        }
    }

    public void Dispose()
    {
        _microphone?.Dispose();
        _systemAudio?.Dispose();
    }
}

public enum AudioSource { Microphone, SystemAudio }