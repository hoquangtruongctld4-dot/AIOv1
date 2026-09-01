using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    public partial class m3u8 : Window
    {
        private string _selectedFolderPath = "";
        private readonly List<DownloadEntryRow> _downloadEntries = new List<DownloadEntryRow>();
        private static readonly HttpClient httpClient = new HttpClient();

        public m3u8()
        {
            InitializeComponent();
            if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            }
            httpClient.Timeout = TimeSpan.FromSeconds(80000);
            AddEntryRow();
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục để lưu file tải về";
                dialog.ShowNewFolderButton = true;
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    _selectedFolderPath = dialog.SelectedPath;
                    FolderLabel.Text = _selectedFolderPath;
                    FolderLabel.Foreground = System.Windows.Media.Brushes.DarkGreen;
                }
            }
        }

        private void AddEntryRowButton_Click(object sender, RoutedEventArgs e)
        {
            AddEntryRow();
        }

        private void AddEntryRow()
        {
            var newEntryRow = new DownloadEntryRow();
            _downloadEntries.Add(newEntryRow);
            EntryRowsPanel.Children.Add(newEntryRow.RowGrid);
            Dispatcher.BeginInvoke(new Action(() => {
                newEntryRow.FilmNameTextBox.Focus();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedFolderPath))
            {
                CustomMessageBox.Show(this, "Vui lòng chọn thư mục lưu trước khi bắt đầu tải.", "Chưa chọn thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateGlobalStatus("Vui lòng chọn thư mục lưu.", true);
                return;
            }

            var validEntriesToDownload = _downloadEntries
                .Select((entry, index) => new { entry.FilmNameTextBox, entry.LinkTextBox, entry })
                .Where(item => !string.IsNullOrWhiteSpace(item.FilmNameTextBox.Text) && Uri.IsWellFormedUriString(item.LinkTextBox.Text.Trim(), UriKind.Absolute))
                .ToList();

            if (!validEntriesToDownload.Any())
            {
                CustomMessageBox.Show(this, "Vui lòng nhập tên phim và link M3U8 hợp lệ vào ít nhất một dòng.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateGlobalStatus("Không có gì để tải. Kiểm tra lại tên phim và link M3U8.", true);
                return;
            }

            StartDownloadButton.IsEnabled = false;
            var downloadTasks = new List<Task>();
            int taskCounter = 0;

            foreach (var item in validEntriesToDownload)
            {
                taskCounter++;
                string filmName = item.FilmNameTextBox.Text.Trim();
                string m3u8Link = item.LinkTextBox.Text.Trim();
                bool mergeToMp4 = MergeMp4CheckBox.IsChecked ?? false;
                DownloadEntryRow currentEntryUI = item.entry;

                currentEntryUI.FilmNameTextBox.IsEnabled = false;
                currentEntryUI.LinkTextBox.IsEnabled = false;
                downloadTasks.Add(DownloadM3u8Content(taskCounter, filmName, m3u8Link, _selectedFolderPath, mergeToMp4, currentEntryUI));
            }

            UpdateGlobalStatus($"Đang xử lý {downloadTasks.Count} mục tải xuống...");
            try
            {
                await Task.WhenAll(downloadTasks);
                UpdateGlobalStatus("Tất cả các tác vụ tải xuống đã hoàn tất (hoặc gặp lỗi).", false);
            }
            catch (Exception)
            {
                UpdateGlobalStatus($"Có lỗi xảy ra trong quá trình xử lý chung.", true);
            }
            finally
            {
                StartDownloadButton.IsEnabled = true;
            }
        }

        private async Task DownloadM3u8Content(int queueIndex, string filmName, string m3u8Url, string baseSavePath, bool shouldMergeToMp4, DownloadEntryRow entryUiControls)
        {
            string currentTaskPrefix = $"[{queueIndex}: {filmName}] ";
            UpdateGlobalStatus($"{currentTaskPrefix}Đang xử lý...");

            Uri m3u8Uri;
            if (!Uri.TryCreate(m3u8Url, UriKind.Absolute, out m3u8Uri))
            {
                UpdateGlobalStatus($"{currentTaskPrefix}Link M3U8 không hợp lệ.", true);
                return;
            }

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(m3u8Uri);
                if (!response.IsSuccessStatusCode)
                {
                    UpdateGlobalStatus($"{currentTaskPrefix}Lỗi tải M3U8. Status: {response.StatusCode}", true);
                }
                response.EnsureSuccessStatusCode();

                string m3u8Content = await response.Content.ReadAsStringAsync();
                string[] lines = m3u8Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                var segmentUrlsFromM3u8 = new List<string>();
                string m3u8Path = m3u8Uri.AbsoluteUri;
                Uri baseUriForSegments = new Uri(m3u8Path.Substring(0, m3u8Path.LastIndexOf('/') + 1));

                string mapSegmentHttpUrl = null;
                foreach (string line in lines.Where(l => l.StartsWith("#EXT-X-MAP", StringComparison.OrdinalIgnoreCase)))
                {
                    var match = Regex.Match(line, "URI=\"([^\"]+)\"");
                    if (match.Success)
                    {
                        mapSegmentHttpUrl = new Uri(baseUriForSegments, match.Groups[1].Value).AbsoluteUri;
                        break;
                    }
                }

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (line.StartsWith("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase))
                        {
                            UpdateGlobalStatus($"{currentTaskPrefix}Stream có thể bị mã hóa.", true);
                        }
                        continue;
                    }
                    segmentUrlsFromM3u8.Add(new Uri(baseUriForSegments, line.Trim()).AbsoluteUri);
                }

                if (!segmentUrlsFromM3u8.Any())
                {
                    UpdateGlobalStatus($"{currentTaskPrefix}Không tìm thấy segment dữ liệu.", true);
                    return;
                }

                string sanitizedFilmName = SanitizeFileName(filmName);
                string downloadFolder = Path.Combine(baseSavePath, sanitizedFilmName);
                Directory.CreateDirectory(downloadFolder);
                string listFilePath = Path.Combine(downloadFolder, "ts_list.txt");
                var segmentFileNamesForConcat = new List<string>();

                if (mapSegmentHttpUrl != null)
                {
                    string mapFileNameOnDisk = "segment_init.ts";
                    string mapFilePathOnDisk = Path.Combine(downloadFolder, mapFileNameOnDisk);
                    try
                    {
                        byte[] mapData = await httpClient.GetByteArrayAsync(mapSegmentHttpUrl);
                        await File.WriteAllBytesAsync(mapFilePathOnDisk, mapData);
                        segmentFileNamesForConcat.Add($"file '{mapFileNameOnDisk}'");
                    }
                    catch (Exception)
                    {
                        UpdateGlobalStatus($"{currentTaskPrefix}Lỗi tải init segment (MAP).", true);
                    }
                }

                int downloadedDataSegmentsCount = 0;
                for (int i = 0; i < segmentUrlsFromM3u8.Count; i++)
                {
                    string currentSegmentHttpUrl = segmentUrlsFromM3u8[i];
                    string segmentFileNameOnDisk = $"segment_{i:D5}.ts";
                    string segmentFilePathOnDisk = Path.Combine(downloadFolder, segmentFileNameOnDisk);
                    try
                    {
                        byte[] segmentData = await httpClient.GetByteArrayAsync(currentSegmentHttpUrl);
                        await File.WriteAllBytesAsync(segmentFilePathOnDisk, segmentData);
                        segmentFileNamesForConcat.Add($"file '{segmentFileNameOnDisk}'");
                        downloadedDataSegmentsCount++;
                    }
                    catch (Exception)
                    {
                        UpdateGlobalStatus($"{currentTaskPrefix}Lỗi tải segment {i + 1}.", true);
                    }
                }

                if (downloadedDataSegmentsCount == 0)
                {
                    UpdateGlobalStatus($"{currentTaskPrefix}Không tải được data segment.", true);
                    return;
                }
                await File.WriteAllLinesAsync(listFilePath, segmentFileNamesForConcat);

                if (shouldMergeToMp4)
                {
                    UpdateGlobalStatus($"{currentTaskPrefix}Đang ghép MP4...");
                    string outputMp4Path = Path.Combine(downloadFolder, sanitizedFilmName + ".mp4");
                    if (File.Exists(outputMp4Path))
                    {
                        try { File.Delete(outputMp4Path); }
                        catch (Exception ex)
                        {
                            UpdateGlobalStatus($"{currentTaskPrefix}Lỗi xóa file MP4 cũ: {ex.Message}", true);
                        }
                    }

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outputMp4Path}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = downloadFolder
                    };

                    using (Process process = new Process { StartInfo = psi })
                    {
                        string ffmpegOutput = "";
                        string ffmpegError = "";

                        process.OutputDataReceived += (s, ev) => { if (ev.Data != null) ffmpegOutput += ev.Data + Environment.NewLine; };
                        process.ErrorDataReceived += (s, ev) => { if (ev.Data != null) ffmpegError += ev.Data + Environment.NewLine; };

                        try
                        {
                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            await process.WaitForExitAsync();

                            if (process.ExitCode == 0 && File.Exists(outputMp4Path) && new FileInfo(outputMp4Path).Length > 0)
                            {
                                UpdateGlobalStatus($"{currentTaskPrefix}Ghép MP4 thành công: {outputMp4Path}");
                            }
                            else
                            {
                                UpdateGlobalStatus($"{currentTaskPrefix}Lỗi ghép MP4. Mã lỗi ffmpeg: {process.ExitCode}. Chi tiết lỗi (nếu có): {ffmpegError.Trim()}", true);
                            }
                        }
                        catch (Exception exFF)
                        {
                            UpdateGlobalStatus($"{currentTaskPrefix}Lỗi khi chạy FFmpeg: {exFF.Message}", true);
                        }
                    }
                }
                else
                {
                    UpdateGlobalStatus($"{currentTaskPrefix}Tải các segment xong (không ghép).");
                }
            }
            catch (HttpRequestException httpEx)
            {
                UpdateGlobalStatus($"{currentTaskPrefix}Lỗi HTTP: {httpEx.StatusCode}.", true);
            }
            catch (Exception ex)
            {
                UpdateGlobalStatus($"{currentTaskPrefix}Lỗi không xác định: {ex.GetType().Name}.", true);
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    entryUiControls.FilmNameTextBox.IsEnabled = true;
                    entryUiControls.LinkTextBox.IsEnabled = true;
                });
            }
        }
        private void UpdateGlobalStatus(string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = $"Trạng thái: {message}";
                StatusLabel.Foreground = isError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Blue;
            });
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled_video";
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            string sanitized = Regex.Replace(name, invalidRegStr, "_");
            sanitized = sanitized.TrimStart('_').TrimEnd('_');
            if (string.IsNullOrWhiteSpace(sanitized)) return "video_segment";
            return sanitized.Length > 100 ? sanitized.Substring(0, 100) : sanitized;
        }
    }

    public class DownloadEntryRow
    {
        public System.Windows.Controls.Grid RowGrid { get; private set; }
        public System.Windows.Controls.TextBox FilmNameTextBox { get; private set; }
        public System.Windows.Controls.TextBox LinkTextBox { get; private set; }

        public DownloadEntryRow()
        {
            RowGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 3) };
            RowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(200) });
            RowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            FilmNameTextBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Nhập tên phim hoặc tên thư mục sẽ được tạo" };
            LinkTextBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Dán link M3U8 vào đây" };

            System.Windows.Controls.Grid.SetColumn(FilmNameTextBox, 0);
            System.Windows.Controls.Grid.SetColumn(LinkTextBox, 1);

            RowGrid.Children.Add(FilmNameTextBox);
            RowGrid.Children.Add(LinkTextBox);
        }
    }
}