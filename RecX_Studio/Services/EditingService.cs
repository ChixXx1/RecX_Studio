using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RecX_Studio.Services
{
    public class EditingService
    {
        public void TrimVideo(string inputPath, string outputPath, TimeSpan startTime, TimeSpan endTime)
        {
            string ffmpegPath = GetFFmpegPath();
            if (!File.Exists(ffmpegPath))
                throw new FileNotFoundException("FFmpeg не найден. Невозможно выполнить редактирование.");

            // Форматируем время в формат HH:mm:ss.fff
            string startArg = startTime.ToString(@"hh\:mm\:ss\.fff");
            string durationArg = (endTime - startTime).ToString(@"hh\:mm\:ss\.fff");

            // Команда FFmpeg для обрезки
            // -ss: начальная точка
            // -t: длительность отрезка
            // -c copy: копирует потоки без перекодирования (очень быстро)
            string arguments = $"-ss {startArg} -i \"{inputPath}\" -t {durationArg} -c copy -avoid_negative_ts 1 \"{outputPath}\"";
            
            Debug.WriteLine($"🔧 Команда FFmpeg для обрезки: {ffmpegPath} {arguments}");

            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(processInfo))
            {
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg завершился с ошибкой. Код: {process.ExitCode}\nДетали: {error}");
                }
            }
        }

        private string GetFFmpegPath()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(currentDir, "ffmpeg.exe");
        }
    }
}