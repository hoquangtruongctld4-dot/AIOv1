using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Waveform
{
    public static class WavePeakCache
    {
        private static readonly string CacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WaveCache");

        static WavePeakCache()
        {
            Directory.CreateDirectory(CacheFolder);
        }

        private static string GetCacheKey(string mediaPath)
        {
            var fileInfo = new FileInfo(mediaPath);
            if (!fileInfo.Exists) return null;

            string keySource = $"{mediaPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{WavePeakPyramid.CurrentVersion}";
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keySource));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static string GetCacheFilePath(string mediaPath)
        {
            string key = GetCacheKey(mediaPath);
            return key != null ? Path.Combine(CacheFolder, $"{key}.a1pk") : null;
        }

        public static bool TryLoad(string mediaPath, out WavePeakPyramid pyramid)
        {
            pyramid = null;
            string cachePath = GetCacheFilePath(mediaPath);
            if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
            {
                return false;
            }

            try
            {
                using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                if (br.ReadUInt32() != WavePeakPyramid.MagicHeader) return false;
                if (br.ReadUInt16() != WavePeakPyramid.CurrentVersion) return false;

                var loadedPyramid = new WavePeakPyramid();
                loadedPyramid.BinsPerSecond = br.ReadInt32();
                int levelCount = br.ReadInt32();

                for (int i = 0; i < levelCount; i++)
                {
                    int arrayLength = br.ReadInt32();
                    var levelData = new float[arrayLength];
                    for (int j = 0; j < arrayLength; j++)
                    {
                        levelData[j] = br.ReadSingle();
                    }
                    loadedPyramid.Levels.Add(levelData);
                }
                pyramid = loadedPyramid;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<WavePeakPyramid> BuildAsync(string mediaPath, CancellationToken ct)
        {
            // Bọc lệnh gọi đồng bộ trong Task.Run để không block UI thread
            var peaksL0 = await Task.Run(() =>
            {
                // Kiểm tra cancellation token trước khi thực hiện tác vụ nặng
                ct.ThrowIfCancellationRequested();
                return FFmpegWaveformExtractor.ExtractPeaks(mediaPath);
            }, ct);

            ct.ThrowIfCancellationRequested();

            var pyramid = new WavePeakPyramid();
            pyramid.Levels.Add(peaksL0);

            // Xây dựng các level cao hơn của pyramid
            while (pyramid.Levels[^1].Length > 256)
            {
                ct.ThrowIfCancellationRequested();
                var prevLevel = pyramid.Levels[^1];
                var nextLevel = new float[(prevLevel.Length + 1) / 2];
                for (int i = 0, j = 0; i < prevLevel.Length; i += 2, j++)
                {
                    float a = prevLevel[i];
                    float b = (i + 1 < prevLevel.Length) ? prevLevel[i + 1] : a;
                    nextLevel[j] = MathF.Max(a, b);
                }
                pyramid.Levels.Add(nextLevel);
            }

            // Lưu cache vào đĩa ở một thread khác để không block
            _ = Task.Run(() => Save(mediaPath, pyramid), CancellationToken.None);
            return pyramid;
        }

        private static void Save(string mediaPath, WavePeakPyramid pyramid)
        {
            string cachePath = GetCacheFilePath(mediaPath);
            if (string.IsNullOrEmpty(cachePath)) return;

            try
            {
                using var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write);
                using var bw = new BinaryWriter(fs);

                bw.Write(WavePeakPyramid.MagicHeader);
                bw.Write(WavePeakPyramid.CurrentVersion);
                bw.Write(pyramid.BinsPerSecond);
                bw.Write(pyramid.Levels.Count);

                foreach (var level in pyramid.Levels)
                {
                    bw.Write(level.Length);
                    foreach (var val in level)
                    {
                        bw.Write(val);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WavePeakCache] Failed to save cache for {mediaPath}: {ex.Message}");
            }
        }
    }
}