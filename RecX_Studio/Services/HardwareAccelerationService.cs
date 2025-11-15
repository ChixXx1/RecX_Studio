// Файл: Services/HardwareAccelerationService.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RecX_Studio.Services;

public enum EncoderType
{
    Software, // libx264
    NVENC,    // NVIDIA
    AMF,      // AMD
    QSV       // Intel Quick Sync
}

public class HardwareAccelerationService
{
    private EncoderType? _availableEncoder;
    private readonly object _lock = new object();

    public EncoderType GetAvailableEncoder()
    {
        // Используем блокировку, чтобы не запускать проверку несколько раз
        lock (_lock)
        {
            if (_availableEncoder.HasValue)
            {
                return _availableEncoder.Value;
            }

            _availableEncoder = DetectEncoder();
            return _availableEncoder.Value;
        }
    }

    private EncoderType DetectEncoder()
    {
        try
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                Debug.WriteLine("❌ FFmpeg не найден, невозможно проверить аппаратные кодировщики.");
                return EncoderType.Software;
            }

            Debug.WriteLine("🔍 Поиск доступных аппаратных кодировщиков...");
            
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -encoders",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // Ищем кодировщики в порядке приоритета: NVIDIA > AMD > Intel
                if (Regex.IsMatch(output, @"h264_nvenc"))
                {
                    Debug.WriteLine("✅ Найден кодировщик NVIDIA NVENC");
                    return EncoderType.NVENC;
                }
                if (Regex.IsMatch(output, @"h264_amf"))
                {
                    Debug.WriteLine("✅ Найден кодировщик AMD AMF");
                    return EncoderType.AMF;
                }
                if (Regex.IsMatch(output, @"h264_qsv"))
                {
                    Debug.WriteLine("✅ Найден кодировщик Intel Quick Sync Video");
                    return EncoderType.QSV;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Ошибка при поиске кодировщиков: {ex.Message}");
        }

        Debug.WriteLine("⚠️ Аппаратные кодировщики не найдены, используется программный.");
        return EncoderType.Software;
    }
}