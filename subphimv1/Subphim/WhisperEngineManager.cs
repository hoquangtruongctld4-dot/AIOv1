using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SharpCompress.Archives; 
using SharpCompress.Common;

namespace subphimv1.Services.Whisper
{
    public class EngineInfo
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public string TargetDirectoryName { get; set; } 
        public string ExecutableName { get; set; } 
        public string DisplayName => $"Tải {Name}";
        public string Size { get; set; }
    }
    public static class WhisperEngineManager
    {
        private static readonly string BaseToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WhisperTools");
        public static readonly Dictionary<string, EngineInfo> SupportedEngines = new Dictionary<string, EngineInfo>
        {
            {
                WhisperEngines.Cpp, new EngineInfo
                {
                    Name = "Whisper.cpp",
                    DownloadUrl = "https://github.com/visecal/cpp/releases/download/v1.7v8/Release.rar",
                    TargetDirectoryName = WhisperEngines.Cpp,
                    ExecutableName = "whisper-cli.exe",
                    Size = "Vài MB"
                }
            },
            {
                WhisperEngines.PurfviewFasterWhisper, new EngineInfo
                {
                    Name = "Purfview's Faster-Whisper-XXL",
                    DownloadUrl = "https://github.com/visecal/qidian-website-revamp-kit-94/releases/download/whisper/Purfview-Whisper-Faster.rar",
                    TargetDirectoryName = WhisperEngines.PurfviewFasterWhisper,
                    ExecutableName = "faster-whisper-xxl.exe",
                    Size = "1.4 GB"
                }
            }
        };

        public static string GetEnginePath(string engineName)
        {
            if (SupportedEngines.TryGetValue(engineName, out var engineInfo))
            {
                return Path.Combine(BaseToolsDirectory, engineInfo.TargetDirectoryName);
            }
            return null;
        }

        public static string GetExecutablePath(string engineName)
        {
            if (SupportedEngines.TryGetValue(engineName, out var engineInfo))
            {
                return Path.Combine(BaseToolsDirectory, engineInfo.TargetDirectoryName, engineInfo.ExecutableName);
            }
            return null;
        }

        public static bool IsEngineInstalled(string engineName)
        {
            string exePath = GetExecutablePath(engineName);
            return !string.IsNullOrEmpty(exePath) && File.Exists(exePath);
        }

        public static async Task DownloadAndInstallAsync(EngineInfo engineInfo, IProgress<double> progress, IProgress<string> status, CancellationToken token)
        {
            Directory.CreateDirectory(BaseToolsDirectory);
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(engineInfo.DownloadUrl)}");
            string targetPath = Path.Combine(BaseToolsDirectory, engineInfo.TargetDirectoryName);

            try
            {
                status.Report("Đang tải file nén engine...");
                var handler = new HttpClientHandler() { AllowAutoRedirect = true };
                using (var client = new HttpClient(handler))
                {
                    using var response = await client.GetAsync(engineInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength;

                    using var contentStream = await response.Content.ReadAsStreamAsync(token);
                    using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var totalBytesRead = 0L;
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                        totalBytesRead += bytesRead;
                        if (totalBytes.HasValue)
                        {
                            progress.Report((double)totalBytesRead / totalBytes.Value * 100);
                        }
                    }
                }
                status.Report("Đang giải nén...");
                progress.Report(100);

                if (Directory.Exists(targetPath))
                {
                    try { Directory.Delete(targetPath, true); } catch { /* Bỏ qua */ }
                }
                Directory.CreateDirectory(targetPath);
                await Task.Run(() =>
                {
                    using (var archive = ArchiveFactory.Open(tempFilePath))
                    {
                        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            token.ThrowIfCancellationRequested();

                           
                            entry.WriteToDirectory(targetPath, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }, token);

                status.Report("Cài đặt thành công!");
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { /* Bỏ qua */ }
                }
            }
        }
    }
}
