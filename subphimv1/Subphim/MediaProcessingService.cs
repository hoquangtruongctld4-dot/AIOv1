using FFMpegCore;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace subphimv1.Services
{
    public static class MediaProcessingService
    {
        // HÀM GenerateVideoThumbnailDataAsync ĐÃ ĐƯỢC XÓA BỎ HOÀN TOÀN TỪ ĐÂY

        public static string ConvertBitmapImageToBase64(BitmapImage bitmapImage)
        {
            if (bitmapImage == null) return null;
            try
            {
                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                using (MemoryStream ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    byte[] imageBytes = ms.ToArray();
                    return Convert.ToBase64String(imageBytes);
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<List<float>> GenerateAudioWaveformAsync(string mediaPath)
        {
            string tempAudioPath = TempFileManager.CreateTempFile(".wav");
            var peaks = new List<float>();

            try
            {
                await FFMpegArguments
                    .FromFileInput(mediaPath)
                    .OutputToFile(tempAudioPath, true, options => options
                        .WithAudioCodec("pcm_s16le")
                        .WithAudioSamplingRate(8000)
                        .WithCustomArgument("-ac 1"))
                    .ProcessAsynchronously();
                using (var reader = new WaveFileReader(tempAudioPath))
                {
                    var sampleProvider = reader.ToSampleProvider().ToMono();
                    int samplesPerPeak = sampleProvider.WaveFormat.SampleRate / 100;
                    var buffer = new float[samplesPerPeak];
                    int bytesRead;
                    while ((bytesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        float max = 0;
                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (Math.Abs(buffer[i]) > max) max = Math.Abs(buffer[i]);
                        }
                        peaks.Add(max);
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                if (File.Exists(tempAudioPath))
                {
                    try { File.Delete(tempAudioPath); } catch { }
                }
            }
            return peaks;
        }
    }
}