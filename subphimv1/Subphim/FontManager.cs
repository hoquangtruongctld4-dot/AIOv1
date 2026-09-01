using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace subphimv1.Subphim
{
    public static class FontManager
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DichTruyenNovel");
        public static readonly string FontFolder = Path.Combine(AppDataFolder, "Fonts");

        static FontManager()
        {
            Directory.CreateDirectory(FontFolder);
        }

        public static List<FontFamily> GetAvailableFonts()
        {
            var fontFamilies = new List<FontFamily>();
            fontFamilies.AddRange(Fonts.SystemFontFamilies.OrderBy(f => f.Source));
            if (Directory.Exists(FontFolder))
            {
                var customFontFiles = Directory.GetFiles(FontFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

                foreach (var fontFile in customFontFiles)
                {
                    try
                    {
                        var uri = new Uri(Path.GetFullPath(fontFile));
                        fontFamilies.Add(new FontFamily(uri, Path.GetFileNameWithoutExtension(fontFile)));
                    }
                    catch { /* */ }
                }
            }
            return fontFamilies.OrderBy(f => f.Source).ToList();
        }

        public static async Task DownloadFontAsync(string url, IProgress<double> progress)
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                var fileName = Path.GetFileName(url);
                var filePath = Path.Combine(FontFolder, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var httpStream = await response.Content.ReadAsStreamAsync())
                {
                    var totalBytesRead = 0L;
                    var buffer = new byte[8192];
                    var isMoreToRead = true;

                    do
                    {
                        var bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (canReportProgress)
                            {
                                progress.Report((double)totalBytesRead / totalBytes * 100);
                            }
                        }
                    } while (isMoreToRead);
                }
            }
        }
    }
}