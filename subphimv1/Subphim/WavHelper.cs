using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace subphimv1.Services
{
    public static class WavHelper
    {

        public static byte[] ConvertToWav(byte[] audioData, string mimeType)
        {

            var (sampleRate, bitsPerSample) = ParseAudioMimeType(mimeType);
            const int numChannels = 1;
            int dataSize = audioData.Length;
            int bytesPerSample = bitsPerSample / 8;
            int blockAlign = numChannels * bytesPerSample;
            int byteRate = sampleRate * blockAlign;
            int chunkSize = 36 + dataSize; 

            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream, Encoding.UTF8))
            {

                writer.Write(Encoding.UTF8.GetBytes("RIFF"));
                writer.Write(chunkSize);
                writer.Write(Encoding.UTF8.GetBytes("WAVE"));

                writer.Write(Encoding.UTF8.GetBytes("fmt "));
                writer.Write(16); // Subchunk1Size (16 cho PCM)
                writer.Write((ushort)1); // AudioFormat (1 cho PCM)
                writer.Write((ushort)numChannels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((ushort)blockAlign);
                writer.Write((ushort)bitsPerSample);

                // "data" sub-chunk
                writer.Write(Encoding.UTF8.GetBytes("data"));
                writer.Write(dataSize);

                // Ghi dữ liệu âm thanh thực tế
                writer.Write(audioData);

                Debug.WriteLine($"[WavHelper] WAV header created successfully. Total WAV size: {memoryStream.Length}");
                return memoryStream.ToArray();
            }
        }

        private static (int sampleRate, int bitsPerSample) ParseAudioMimeType(string mimeType)
        {
            int rate = 24000;
            int bits = 16;

            if (string.IsNullOrEmpty(mimeType))
            {
                return (rate, bits);
            }
            var rateMatch = Regex.Match(mimeType, @"rate=(\d+)");
            if (rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out int parsedRate))
            {
                rate = parsedRate;
            }
            var bitsMatch = Regex.Match(mimeType, @"audio/L(\d+)");
            if (bitsMatch.Success && int.TryParse(bitsMatch.Groups[1].Value, out int parsedBits))
            {
                bits = parsedBits;
            }

            Debug.WriteLine($"[WavHelper] Parsed MimeType: Rate={rate}, BitsPerSample={bits}");
            return (rate, bits);
        }
    }
}