using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace subphimv1.Services
{
    public class GpuOption
    {
        public string DisplayName { get; set; }
        public string FfmpegValue { get; set; } // hwaccel value (cuda, qsv, d3d11va)
        public string RecommendedCodec { get; set; } // encoder value (h264_nvenc, etc.)

        public override string ToString() => DisplayName;
    }

    public static class FfmpegGpuDetector
    {
        private static List<GpuOption> _cachedGpuOptions = null;

        public static async Task<List<GpuOption>> GetAvailableGpusAsync()
        {
            if (_cachedGpuOptions != null)
            {
                return _cachedGpuOptions;
            }

            var options = new List<GpuOption>
            {
                new GpuOption { DisplayName = "Không dùng (CPU Software)", FfmpegValue = "none", RecommendedCodec = "libx264" }
            };

            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                _cachedGpuOptions = options;
                return options;
            }

            // --- LOGIC MỚI: KIỂM TRA TỪNG GPU MỘT CÁCH THỰC TẾ ---

            // 1. Kiểm tra NVIDIA (NVENC)
            if (await IsEncoderAvailable(ffmpegPath, "h264_nvenc"))
            {
                options.Add(new GpuOption { DisplayName = "NVIDIA (NVENC)", FfmpegValue = "cuda", RecommendedCodec = "h264_nvenc" });
            }

            // 2. Kiểm tra Intel (QSV)
            if (await IsEncoderAvailable(ffmpegPath, "h264_qsv"))
            {
                options.Add(new GpuOption { DisplayName = "Intel (Quick Sync)", FfmpegValue = "qsv", RecommendedCodec = "h264_qsv" });
            }

            // 3. Kiểm tra AMD (AMF)
            if (await IsEncoderAvailable(ffmpegPath, "h264_amf"))
            {
                options.Add(new GpuOption { DisplayName = "AMD (AMF/VCE)", FfmpegValue = "d3d11va", RecommendedCodec = "h264_amf" });
            }

            _cachedGpuOptions = options.ToList(); // ToList để tạo bản sao
            return _cachedGpuOptions;
        }

        private static async Task<bool> IsEncoderAvailable(string ffmpegPath, string encoderName)
        {
            string arguments = $"-f lavfi -i nullsrc=s=64x64:d=1 -c:v {encoderName} -frames:v 1 -f null -";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardError = true, // Lỗi sẽ được ghi vào đây
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            try
            {
                process.Start();
                // Chúng ta không cần đọc output, chỉ cần đợi nó kết thúc
                await process.WaitForExitAsync();

                // Nếu ExitCode là 0, nghĩa là lệnh đã thành công -> encoder hoạt động.
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                // Bất kỳ lỗi nào khi chạy process cũng có nghĩa là không thành công.
                return false;
            }
        }

        public static GpuOption GetBestAvailableGpu(List<GpuOption> availableGpus)
        {
            if (availableGpus == null || availableGpus.Count <= 1)
            {
                return availableGpus?.FirstOrDefault(g => g.FfmpegValue == "none");
            }

            var bestGpu = availableGpus.FirstOrDefault(g => g.FfmpegValue == "cuda")
                       ?? availableGpus.FirstOrDefault(g => g.FfmpegValue == "qsv")
                       ?? availableGpus.FirstOrDefault(g => g.FfmpegValue == "d3d11va");

            return bestGpu ?? availableGpus.FirstOrDefault(g => g.FfmpegValue != "none");
        }
    }
}