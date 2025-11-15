using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecX_Studio.Services;

public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture _loopbackCapture;
    private WasapiCapture _microphoneCapture;
    private BufferedWaveProvider _loopbackWaveProvider;
    private BufferedWaveProvider _microphoneWaveProvider;
    private bool _isRecording;

    public List<AudioDeviceInfo> GetAudioInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            
            foreach (var device in captureDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    Name = device.FriendlyName,
                    Id = device.ID,
                    Type = AudioDeviceType.Input
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка получения аудиоустройств ввода: {ex.Message}");
        }

        return devices;
    }

    public List<AudioDeviceInfo> GetAudioOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            
            foreach (var device in renderDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    Name = device.FriendlyName,
                    Id = device.ID,
                    Type = AudioDeviceType.Output
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка получения аудиоустройств вывода: {ex.Message}");
        }

        return devices;
    }

    public void StartAudioCapture(string outputDeviceId = null, string inputDeviceId = null)
    {
        if (_isRecording) return;

        try
        {
            // Захват системного звука (выходного аудио)
            if (!string.IsNullOrEmpty(outputDeviceId))
            {
                var enumerator = new MMDeviceEnumerator();
                var outputDevice = enumerator.GetDevice(outputDeviceId);
                
                _loopbackCapture = new WasapiLoopbackCapture(outputDevice);
                _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
                _loopbackWaveProvider = new BufferedWaveProvider(_loopbackCapture.WaveFormat);
                
                _loopbackCapture.StartRecording();
                Debug.WriteLine($"🔊 Захват системного звука начат: {outputDevice.FriendlyName}");
            }

            // Захват микрофона (входного аудио)
            if (!string.IsNullOrEmpty(inputDeviceId))
            {
                var enumerator = new MMDeviceEnumerator();
                var inputDevice = enumerator.GetDevice(inputDeviceId);
                
                _microphoneCapture = new WasapiCapture(inputDevice);
                _microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
                _microphoneWaveProvider = new BufferedWaveProvider(_microphoneCapture.WaveFormat);
                
                _microphoneCapture.StartRecording();
                Debug.WriteLine($"🎤 Захват микрофона начат: {inputDevice.FriendlyName}");
            }

            _isRecording = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка запуска захвата аудио: {ex.Message}");
        }
    }

    public void StopAudioCapture()
    {
        try
        {
            _loopbackCapture?.StopRecording();
            _microphoneCapture?.StopRecording();
            
            _loopbackCapture?.Dispose();
            _microphoneCapture?.Dispose();
            
            _loopbackCapture = null;
            _microphoneCapture = null;
            _isRecording = false;
            
            Debug.WriteLine("🔇 Захват аудио остановлен");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка остановки захвата аудио: {ex.Message}");
        }
    }

    private void OnLoopbackDataAvailable(object sender, WaveInEventArgs e)
    {
        _loopbackWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnMicrophoneDataAvailable(object sender, WaveInEventArgs e)
    {
        _microphoneWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    public string GetAudioFilters(string outputDeviceId, string inputDeviceId, int audioBitrate)
    {
        var filters = new List<string>();

        if (!string.IsNullOrEmpty(outputDeviceId))
        {
            // Для системного звука используем виртуальное устройство (в Windows это обычно stereo mix)
            filters.Add($"-f dshow -i audio=\"virtual-audio-capturer\" -ac 2 -b:a {audioBitrate}k");
        }

        if (!string.IsNullOrEmpty(inputDeviceId))
        {
            // Для микрофона
            filters.Add($"-f dshow -i audio=\"microphone\" -ac 1 -b:a {audioBitrate}k");
        }

        return string.Join(" ", filters);
    }

    public void Dispose()
    {
        StopAudioCapture();
    }
}

public class AudioDeviceInfo
{
    public string Name { get; set; }
    public string Id { get; set; }
    public AudioDeviceType Type { get; set; }
}

public enum AudioDeviceType
{
    Input,
    Output
}