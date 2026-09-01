using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Subphim
{
    public class VsfService
    {
        public Action<string, bool> LogMessage { get; set; }
        public Action<int> OnProgress { get; set; }

        private const string DefaultApiKey = "0365912903Aa@";  // Default API key

        private void InternalLog(string message, bool isError = false)
        {
            LogMessage?.Invoke($"[FindSub] {message}", isError);
        }

        public class VsfParameters
        {
            public string VsfPath { get; set; }
            public string VideoPath { get; set; }
            public string OutputBaseDirectory { get; set; }
            public string OcrImagesFolderName { get; set; }
            public float CropTop { get; set; }
            public float CropBottom { get; set; }
            public float CropLeft { get; set; }
            public float CropRight { get; set; }
            public VsfProcessingMode ProcessingMode { get; set; }
            public VsfVideoOpenMethod OpenMethod { get; set; }
            public bool UseCuda { get; set; }
            public string NumThreadsSearch { get; set; }
            public string NumThreadsClean { get; set; }
            public string AdditionalArgs { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public bool ClearDirectories { get; set; } = true;
            public string SpecificOutputDirectory { get; set; }
        }

        public async Task<(bool success, string resultPath)> RunVSFProcessAsync(VsfParameters p, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(p.VideoPath) || !File.Exists(p.VideoPath))
            {
                return (false, null);
            }
            if (string.IsNullOrWhiteSpace(p.VsfPath) || !File.Exists(p.VsfPath))
            {
                return (false, null);
            }
            if (p.ClearDirectories)
            {
                try
                {
                    string rgbImagesPath = Path.Combine(p.OutputBaseDirectory, "RGBImages");
                    string txtImagesPath = Path.Combine(p.OutputBaseDirectory, p.OcrImagesFolderName);
                    string ilaImagesPath = Path.Combine(p.OutputBaseDirectory, "ILAImages");
                    string isaImagesPath = Path.Combine(p.OutputBaseDirectory, "ISAImages");

                    if (Directory.Exists(rgbImagesPath)) Directory.Delete(rgbImagesPath, true);
                    if (Directory.Exists(txtImagesPath)) Directory.Delete(txtImagesPath, true);
                    if (Directory.Exists(ilaImagesPath)) Directory.Delete(ilaImagesPath, true);
                    if (Directory.Exists(isaImagesPath)) Directory.Delete(isaImagesPath, true);

                    Directory.CreateDirectory(rgbImagesPath);
                    Directory.CreateDirectory(txtImagesPath);
                    Directory.CreateDirectory(ilaImagesPath);
                    Directory.CreateDirectory(isaImagesPath);
                }
                catch (Exception ex)
                {
                    return (false, null);
                }
            }
            var argsBuilder = new StringBuilder();
            argsBuilder.Append($"--apikey \"{DefaultApiKey}\" ");
            argsBuilder.Append("-r ");

            if (p.ProcessingMode == VsfProcessingMode.CleanAndCreateTxtImages)
            {
                argsBuilder.Append("-ccti ");
            }
            else
            {
            }
            switch (p.OpenMethod)
            {
                case VsfVideoOpenMethod.OpenCV: argsBuilder.Append("--open_video_opencv "); break;
                case VsfVideoOpenMethod.FFmpeg: argsBuilder.Append("--open_video_ffmpeg "); break;
            }
            if (p.UseCuda) argsBuilder.Append("--use_cuda ");

            argsBuilder.Append($"-i \"{p.VideoPath}\" ");
            string finalOutputDir = !string.IsNullOrWhiteSpace(p.SpecificOutputDirectory)
                        ? p.SpecificOutputDirectory
                        : p.OutputBaseDirectory;
            Directory.CreateDirectory(finalOutputDir);

            argsBuilder.Append($"-o \"{finalOutputDir.TrimEnd('\\')}\" ");
            argsBuilder.Append($"-te {p.CropTop.ToString(CultureInfo.InvariantCulture)} ");
            argsBuilder.Append($"-be {p.CropBottom.ToString(CultureInfo.InvariantCulture)} ");
            argsBuilder.Append($"-le {p.CropLeft.ToString(CultureInfo.InvariantCulture)} ");
            argsBuilder.Append($"-re {p.CropRight.ToString(CultureInfo.InvariantCulture)} ");

            if (int.TryParse(p.NumThreadsSearch, out int nts) && nts != -1) argsBuilder.Append($"--num_threads {nts} ");
            if (int.TryParse(p.NumThreadsClean, out int ntc) && ntc != -1) argsBuilder.Append($"--num_ocr_threads {ntc} ");

            if (!string.IsNullOrEmpty(p.StartTime)) argsBuilder.Append($"-s {p.StartTime} ");
            if (!string.IsNullOrEmpty(p.EndTime)) argsBuilder.Append($"-e {p.EndTime} ");

            if (!string.IsNullOrWhiteSpace(p.AdditionalArgs))
            {
                argsBuilder.Append(p.AdditionalArgs.Trim() + " ");
            }
            string arguments = argsBuilder.ToString().Trim();
            Debug.Write($"Executing VSF with arguments: {arguments}");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = p.VsfPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(p.VsfPath)
            };

            using (Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                try
                {
                    process.OutputDataReceived += (s, evt) =>
                    {
                        if (evt.Data != null)
                        {
                            var m = Regex.Match(evt.Data.Trim(), @"%\s*(\d+)");
                            if (m.Success && int.TryParse(m.Groups[1].Value, out int prog)) OnProgress?.Invoke(prog);
                        }
                    };
                    process.ErrorDataReceived += (s, evt) =>
                    {
                        if (evt.Data != null)
                        {
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(token);

                    if (process.ExitCode == 255 || process.ExitCode == -1 || process.ExitCode == 0)
                    {
                        string imageFolderName = p.ProcessingMode == VsfProcessingMode.CleanAndCreateTxtImages
                                                  ? p.OcrImagesFolderName : "RGBImages";
                        string correctResultPath = Path.Combine(finalOutputDir, imageFolderName);
                        InternalLog($"VSF thành công. Đường dẫn ảnh trả về: {correctResultPath}");

                        return (true, correctResultPath);
                    }
                    else
                    {
                        return (false, null);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(true);
                        }
                        catch (Exception ex)
                        {
                        }
                    }
                    return (false, null);
                }
                catch (Exception ex)
                {
                    return (false, null);
                }
            }
        }
    }
}
