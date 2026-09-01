using System;
using System.IO;
using FFmpeg.AutoGen;

namespace subphimv1.Filmstrip
{
    public static class FFmpegLoader
    {
        private static bool _inited;
        public static void EnsureRegistered()
        {
            if (_inited) return;

            // Lấy đường dẫn của thư mục chứa file exe
            string ffmpegFolder = AppDomain.CurrentDomain.BaseDirectory;

            // Đoạn code này vẫn hữu ích để đảm bảo hệ điều hành có thể tìm thấy DLL
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Contains(ffmpegFolder, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", ffmpegFolder + ";" + path);

            // Chỉ định RootPath là thư mục gốc của ứng dụng
            ffmpeg.RootPath = ffmpegFolder;
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            ffmpeg.avformat_network_init();
            _inited = true;
        }
    }
}