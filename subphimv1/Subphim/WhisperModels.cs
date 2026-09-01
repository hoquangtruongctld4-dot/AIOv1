using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace subphimv1.Services.Whisper
{
    // Interface chung cho các loại model provider
    public interface IWhisperModelProvider
    {
        string ModelFolder { get; }
        void CreateModelFolder();
        List<WhisperModelInfo> GetModels();
    }

    // Class chứa thông tin về một model cụ thể
    public class WhisperModelInfo
    {
        public string Name { get; set; }
        public string Size { get; set; }
        public string[] Urls { get; set; }
        public string Folder { get; set; } // Dùng cho CTranslate2 và Purfview
        public bool RenameOnDownload { get; set; }

        public string DisplayName => $"{Name} ({Size})";
    }

    // Các hằng số cho Engine
    public static class WhisperEngines
    {
        public const string Cpp = "CPP";
        public const string CTranslate2 = "CTranslate2";
        public const string ConstMe = "Const-me";
        public const string PurfviewFasterWhisper = "Purfview's Faster-Whisper";
        // Thêm các engine khác nếu cần
    }

    // Class quản lý chính
    public static class WhisperModelManager
    {
        public static IWhisperModelProvider GetProvider(string engine)
        {
            switch (engine)
            {
                case WhisperEngines.Cpp:
                    return new WhisperCppModelProvider();
                case WhisperEngines.CTranslate2:
                    return new WhisperCTranslate2ModelProvider();
                case WhisperEngines.PurfviewFasterWhisper: // Dòng mới
                    return new PurfviewFasterWhisperModelProvider(); // Dòng mới
                default:
                    return new WhisperCppModelProvider(); // Mặc định
            }
        }
    }
    public class PurfviewFasterWhisperModelProvider : IWhisperModelProvider
    {
        // Model của Purfview được lưu trong thư mục con "_models" của chính engine đó
        public string ModelFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WhisperTools", WhisperEngines.PurfviewFasterWhisper, "_models");
        private readonly string[] _fileNames = { "model.bin", "config.json", "vocabulary.txt", "vocabulary.json", "tokenizer.json", "preprocessor_config.json" };

        public void CreateModelFolder() => Directory.CreateDirectory(ModelFolder);
        private string[] MakeUrls(string baseUrl) => _fileNames.Select(f => $"{baseUrl.TrimEnd('/')}/{f}").ToArray();

        public List<WhisperModelInfo> GetModels()
        {
            return new List<WhisperModelInfo>
        {
            new WhisperModelInfo { Name = "large-v2", Size = "3.09 GB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-large-v2/resolve/main"), Folder = "faster-whisper-large-v2" },
            new WhisperModelInfo { Name = "medium.en", Size = "1.5 GB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-medium.en/resolve/main"), Folder = "faster-whisper-medium.en" },
            // ... thêm các model khác từ file WhisperPurfviewFasterWhisperModel.cs nếu muốn
        };
        }
    }

    // Provider cho model CPP
    public class WhisperCppModelProvider : IWhisperModelProvider
    {
        public string ModelFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WhisperModels", "CPP");

        public void CreateModelFolder() => Directory.CreateDirectory(ModelFolder);

        public List<WhisperModelInfo> GetModels()
        {
            const string DownloadUrlPrefix = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
            return new List<WhisperModelInfo>
            {
                new WhisperModelInfo { Name = "tiny.en", Size = "74 MB", Urls = new[] { DownloadUrlPrefix + "ggml-tiny.en.bin" } },
                new WhisperModelInfo { Name = "tiny", Size = "74 MB", Urls = new[] { DownloadUrlPrefix + "ggml-tiny.bin" } },
                new WhisperModelInfo { Name = "base.en", Size = "141 MB", Urls = new[] { DownloadUrlPrefix + "ggml-base.en.bin" } },
                new WhisperModelInfo { Name = "base", Size = "141 MB", Urls = new[] { DownloadUrlPrefix + "ggml-base.bin" } },
                new WhisperModelInfo { Name = "small.en", Size = "465 MB", Urls = new[] { DownloadUrlPrefix + "ggml-small.en.bin" } },
                new WhisperModelInfo { Name = "small", Size = "465 MB", Urls = new[] { DownloadUrlPrefix + "ggml-small.bin" } },
                new WhisperModelInfo { Name = "medium.en", Size = "1.42 GB", Urls = new[] { DownloadUrlPrefix + "ggml-medium.en.bin" } },
                new WhisperModelInfo { Name = "medium", Size = "1.42 GB", Urls = new[] { DownloadUrlPrefix + "ggml-medium.bin" } },
                new WhisperModelInfo { Name = "large-v2", Size = "3.09 GB", Urls = new[] { DownloadUrlPrefix + "ggml-large-v2.bin" } },
            };
        }
    }

    // Provider cho model CTranslate2
    public class WhisperCTranslate2ModelProvider : IWhisperModelProvider
    {
        public string ModelFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WhisperModels", "CTranslate2");
        private readonly string[] _fileNames = { "model.bin", "config.json", "vocabulary.txt", "tokenizer.json" };

        public void CreateModelFolder() => Directory.CreateDirectory(ModelFolder);

        private string[] MakeUrls(string baseUrl) => _fileNames.Select(f => $"{baseUrl.TrimEnd('/')}/{f}").ToArray();

        public List<WhisperModelInfo> GetModels()
        {
            return new List<WhisperModelInfo>
            {
                new WhisperModelInfo { Name = "large-v3", Size = "3.1 GB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-large-v3/resolve/main"), Folder = "large-v3" },
                new WhisperModelInfo { Name = "large-v2", Size = "3.1 GB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-large-v2/resolve/main"), Folder = "large-v2" },
                new WhisperModelInfo { Name = "medium.en", Size = "1.5 GB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-medium.en/resolve/main"), Folder = "medium.en" },
                new WhisperModelInfo { Name = "small", Size = "472 MB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-small/resolve/main"), Folder = "small" },
                new WhisperModelInfo { Name = "base", Size = "142 MB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-base/resolve/main"), Folder = "base" },
                new WhisperModelInfo { Name = "tiny", Size = "74 MB", Urls = MakeUrls("https://huggingface.co/Systran/faster-whisper-tiny/resolve/main"), Folder = "tiny" },
            };
        }
    }
}