using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using subphimv1.Services.Whisper;

namespace subphimv1.Services
{
    public class WhisperService
    {
        public class WhisperParameters
        {
            public string Engine { get; set; }
            public string Device { get; set; }
            public string ComputeType { get; set; }
            public string VideoPath { get; set; }
            public string ModelName { get; set; }
            public string Language { get; set; }
            public string OutputSrtPath { get; set; }
            public string AdditionalArgs { get; set; }
            public bool TranslateToEnglish { get; set; }
        }

        public Action<string, bool> LogMessage { get; set; }
        public Action<int> OnProgress { get; set; }

        private void InternalLog(string message, bool isError = false)
        {
            LogMessage?.Invoke($"[WHISPER][{DateTime.Now:HH:mm:ss}] {message}", isError);
        }

        public async Task<bool> RunAsync(WhisperParameters p, CancellationToken token)
        {
            var provider = WhisperModelManager.GetProvider(p.Engine);
            string modelPath;

            if (p.Engine == WhisperEngines.Cpp)
            {
                modelPath = Path.Combine(provider.ModelFolder, $"ggml-{p.ModelName}.bin");
            }
            else if (p.Engine == WhisperEngines.PurfviewFasterWhisper)
            {
                var modelInfo = provider.GetModels().FirstOrDefault(m => m.Name.Replace("* ", "").Trim() == p.ModelName);
                if (modelInfo == null || string.IsNullOrEmpty(modelInfo.Folder))
                {
                    InternalLog($"Không tìm thấy thông tin thư mục cho model '{p.ModelName}' của engine '{p.Engine}'.", true);
                    return false; // Trả về false ngay lập tức
                }
                modelPath = Path.Combine(provider.ModelFolder, modelInfo.Folder);
            }
            else
            {
                modelPath = Path.Combine(provider.ModelFolder, p.ModelName);
            }

            string executablePath = WhisperEngineManager.GetExecutablePath(p.Engine);

            if (!File.Exists(executablePath))
            {
                InternalLog($"Không tìm thấy file thực thi cho engine '{p.Engine}' tại: {executablePath}", true);
                return false;
            }

            if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
            {
                InternalLog($"Không tìm thấy model '{p.ModelName}' cho engine '{p.Engine}' tại: {modelPath}", true);
                return false;
            }

            string tempWavPath = TempFileManager.CreateTempFile(".wav");
            InternalLog("Đang tách luồng âm thanh từ video...");
            bool audioExtracted = await ExtractAudioAsync(p.VideoPath, tempWavPath, token);
            if (!audioExtracted)
            {
                InternalLog("Tách âm thanh thất bại.", true);
                TempFileManager.CleanupCurrentSessionFiles();
                return false;
            }
            InternalLog($"Âm thanh đã được tách ra: {tempWavPath}");

            var argsBuilder = new StringBuilder();
            BuildArguments(argsBuilder, p, modelPath, tempWavPath);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = argsBuilder.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Path.GetDirectoryName(executablePath)
                };

                InternalLog($"Executing: {startInfo.FileName} {startInfo.Arguments}");

                using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
                {
                    process.ErrorDataReceived += (s, evt) => {
                        if (evt.Data != null) InternalLog(evt.Data);
                    };
                    process.OutputDataReceived += (s, evt) => {
                        if (evt.Data != null) InternalLog(evt.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(token);

                    if (token.IsCancellationRequested)
                    {
                        InternalLog("Tiến trình Whisper bị hủy.", true);
                        return false; // Trả về false khi bị hủy
                    }

                    return process.ExitCode == 0; // Trả về true nếu thành công, false nếu thất bại
                }
            }
            catch (Exception ex)
            {
                InternalLog($"Lỗi nghiêm trọng khi chạy Whisper: {ex.Message}", true);
                return false; // Trả về false khi có exception
            }
            finally
            {
                TempFileManager.CleanupCurrentSessionFiles();
            }
        }
        private string GetExecutablePath(string engine)
        {
            // Thay vì đường dẫn cứng, gọi manager để lấy đường dẫn động
            return WhisperEngineManager.GetExecutablePath(engine);
        }

        private void BuildArguments(StringBuilder sb, WhisperParameters p, string modelPath, string audioPath)
        {
            // Xây dựng tham số cho từng Engine
            if (p.Engine == WhisperEngines.Cpp)
            {
                sb.Append($"-m \"{modelPath}\" ");
                sb.Append($"-f \"{audioPath}\" ");
                sb.Append($"-l {p.Language} ");
                sb.Append("-osrt ");
                sb.Append("-nt ");
                if (p.TranslateToEnglish)
                {
                    sb.Append("-tr ");
                }

                 sb.Append($"--device {p.Device} "); 
            }
            else if (p.Engine == WhisperEngines.PurfviewFasterWhisper)
            {
                sb.Append($"--model \"{p.ModelName}\" ");
                sb.Append($"--model_dir \"{Path.GetDirectoryName(modelPath)}\" ");

                if (p.Language != "auto")
                {
                    sb.Append($"--language {p.Language} ");
                }

                if (p.TranslateToEnglish)
                {
                    sb.Append("--task translate ");
                }
                else
                {
                    sb.Append("--task transcribe ");
                }

                sb.Append("--output_format srt ");

                // === SỬ DỤNG GIÁ TRỊ TỪ GIAO DIỆN ===
                sb.Append($"--device {p.Device} ");
                sb.Append($"--compute_type {p.ComputeType} ");
                // ===================================
            }

            // Thêm các tham số tùy chỉnh từ người dùng (ghi đè lên các preset nếu trùng)
            if (!string.IsNullOrWhiteSpace(p.AdditionalArgs))
            {
                sb.Append(p.AdditionalArgs + " ");
            }

            // Thêm file audio vào cuối cho các engine không phải CPP
            if (p.Engine != WhisperEngines.Cpp)
            {
                sb.Append($"\"{audioPath}\"");
            }
        }

        private async Task<bool> ExtractAudioAsync(string videoPath, string audioOutputPath, CancellationToken token)
        {
            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    InternalLog("ffmpeg.exe không tìm thấy trong thư mục ứng dụng.", true);
                    return false;
                }

                // Lệnh ffmpeg để trích xuất audio thành file WAV 16-bit, 16kHz, mono
                string args = $"-i \"{videoPath}\" -y -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{audioOutputPath}\"";

                var startInfo = new ProcessStartInfo(ffmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    // Chờ tiến trình kết thúc
                    await process.WaitForExitAsync(token);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                InternalLog($"Lỗi khi tách audio: {ex.Message}", true);
                return false;
            }
        }
    }
}