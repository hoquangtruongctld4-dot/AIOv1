using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using System.Windows.Threading;

namespace subphimv1
{
    public partial class TtsWindow : Window
    {
        private readonly HttpClient httpClient = new HttpClient();
        private readonly string baseApiUrl = "http://localhost:5050";

        private List<FullVoiceInfo> allVoices;
        private List<SrtLine> loadedSrtLines;
        private string srtFilePath;
        private string outputFolderPath;

        // Thêm các biến để quản lý tiến trình
        private Process ttsServerProcess;
        private CancellationTokenSource batchCancellationTokenSource;
        private MediaPlayer player;
        private string currentTempAudioPath; // Lưu đường dẫn file mp3 tạm thời
        private bool isPlaying = false;
        private DispatcherTimer playbackTimer; // Timer để cập nhật UI
        private bool isDraggingSlider = false;
        public TtsWindow()
        {
            InitializeComponent();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            string apiKey = "your_api_key_here";
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            playbackTimer = new DispatcherTimer();
            playbackTimer.Interval = TimeSpan.FromMilliseconds(100); // Cập nhật 10 lần mỗi giây
            playbackTimer.Tick += PlaybackTimer_Tick;
            player = new MediaPlayer();
            // Sự kiện này sẽ được kích hoạt khi âm thanh phát xong
            player.MediaEnded += Player_MediaEnded;
            this.Closing += TtsWindow_Closing;
        }
        private void Player_MediaEnded(object sender, EventArgs e)
        {
            isPlaying = false;
            playbackTimer.Stop(); // Dừng timer
            player.Stop();

            Dispatcher.Invoke(() => {
                PlayPauseButton.Content = "▶ Play";
                // Reset slider về vị trí đầu khi kết thúc
                TimelineSlider.Value = 0;
                TimeProgressText.Text = "00:00";
            });
        }
        private void PlaybackTimer_Tick(object sender, EventArgs e)
        {
            // Chỉ cập nhật slider nếu người dùng không đang kéo nó
            if (!isDraggingSlider && player != null && player.Source != null && player.NaturalDuration.HasTimeSpan)
            {
                TimelineSlider.Value = player.Position.TotalSeconds;
                TimeProgressText.Text = FormatTimeSpan(player.Position);
            }
        }

        // Hàm xử lý khi người dùng bắt đầu kéo slider
        private void TimelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            isDraggingSlider = true;
        }

        // Hàm xử lý khi người dùng thả slider ra
        private void TimelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (player != null && player.Source != null)
            {
                player.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
            }
            isDraggingSlider = false;
        }

        // Hàm helper để định dạng TimeSpan thành chuỗi mm:ss
        private string FormatTimeSpan(TimeSpan ts)
        {
            return string.Format("{0:00}:{1:00}", (int)ts.TotalMinutes, ts.Seconds);
        }
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (player == null || player.Source == null) return;

            if (isPlaying)
            {
                player.Pause();
                playbackTimer.Stop(); // Dừng timer khi pause
                PlayPauseButton.Content = "▶ Play";
            }
            else
            {
                player.Play();
                playbackTimer.Start(); // Bắt đầu timer khi play
                PlayPauseButton.Content = "❚❚ Pause";
            }
            isPlaying = !isPlaying;
        }
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentTempAudioPath) || !File.Exists(currentTempAudioPath))
            {
                CustomMessageBox.Show("Không tìm thấy file âm thanh để tải xuống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "MP3 Audio File (*.mp3)|*.mp3",
                Title = "Lưu file âm thanh",
                FileName = "speech.mp3"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(currentTempAudioPath, saveFileDialog.FileName, true);
                    CustomMessageBox.Show("Đã lưu file thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Lỗi khi lưu file: {ex.Message}", "Lỗi Lưu File", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            StartServerButton.IsEnabled = false;
            ServerStatusText.Text = "Đang khởi động server, vui lòng chờ...";

            // 1. Bắt đầu chạy tiến trình server
            StartTtsServer();

            // 2. Chờ cho đến khi server sẵn sàng
            bool isServerReady = await WaitForServerReadyAsync();

            if (isServerReady)
            {
                ServerStatusText.Text = "Server đã sẵn sàng! Đang tải danh sách giọng nói...";

                // 3. Tải danh sách giọng nói
                await LoadVoicesAsync();

                // 4. Ẩn lớp phủ và kích hoạt giao diện chính
                ServerStartOverlay.Visibility = Visibility.Collapsed;
                MainContentGrid.IsEnabled = true;
            }
            else
            {
                ServerStatusText.Text = "Không thể kết nối đến server sau 30 giây.";
                CustomMessageBox.Show("Không thể khởi động hoặc kết nối đến server. Vui lòng kiểm tra xem có chương trình diệt virus nào chặn file không và thử lại.", "Lỗi Kết Nối Server", MessageBoxButton.OK, MessageBoxImage.Error);
                StartServerButton.IsEnabled = true; // Cho phép người dùng thử lại
            }
        }
        #region Window & Server Process Management
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }
        private async Task<bool> WaitForServerReadyAsync(int timeoutSeconds = 30)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                try
                {
                    // Thử gửi một request nhẹ nhất có thể (ví dụ: lấy danh sách models)
                    // để kiểm tra xem server có phản hồi không.
                    var response = await httpClient.GetAsync($"{baseApiUrl}/v1/models");
                    if (response.IsSuccessStatusCode)
                    {
                        stopwatch.Stop();
                        return true; // Server đã sẵn sàng
                    }
                }
                catch (HttpRequestException)
                {
                    // Bỏ qua lỗi kết nối và thử lại sau một chút
                }

                // Chờ 500ms trước khi thử lại
                await Task.Delay(500);
            }

            stopwatch.Stop();
            return false; // Hết thời gian chờ mà server vẫn chưa sẵn sàng
        }
        private void TtsWindow_Closing(object sender, CancelEventArgs e)
        {
            // Hủy tác vụ đang chạy nếu có
            batchCancellationTokenSource?.Cancel();
            try
            {
                if (player != null && player.Source != null)
                {
                    player.Close();
                }
                if (!string.IsNullOrEmpty(currentTempAudioPath) && File.Exists(currentTempAudioPath))
                {
                    File.Delete(currentTempAudioPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Không thể dọn dẹp file audio tạm: {ex.Message}");
            }

            // Tắt tiến trình server
            try
            {
                if (ttsServerProcess != null && !ttsServerProcess.HasExited)
                {
                    ttsServerProcess.Kill();
                    ttsServerProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Ghi log nếu cần
                Debug.WriteLine($"Không thể tắt server TTS.exe: {ex.Message}");
            }
        }

        private void StartTtsServer()
        {
            try
            {
                // Kiểm tra xem server đã chạy chưa
                if (Process.GetProcessesByName("TTS").Any())
                {
                    Debug.WriteLine("Server TTS.exe đã chạy.");
                    return;
                }

                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string serverExePath = Path.Combine(appDirectory, "TTS.exe");

                if (!File.Exists(serverExePath))
                {
                    CustomMessageBox.Show("Không tìm thấy file TTS.exe trong thư mục gốc của ứng dụng.", "Lỗi Thiếu File", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo(serverExePath)
                {
                    // Chạy ngầm, không hiển thị cửa sổ console
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = appDirectory
                };

                ttsServerProcess = Process.Start(startInfo);
                Debug.WriteLine("Đã khởi động server TTS.exe.");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể khởi động server TTS.exe.\nChi tiết: {ex.Message}", "Lỗi Khởi Động Server", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { this.Close(); }
        #endregion

        #region Voice Loading and Filtering
        private async Task LoadVoicesAsync()
        {
            try
            {
                var response = await httpClient.GetAsync($"{baseApiUrl}/v1/voices/all");
                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    var voiceResponse = JsonConvert.DeserializeObject<VoiceResponse>(jsonContent);
                    allVoices = voiceResponse?.voices;

                    if (allVoices != null && allVoices.Any())
                    {
                        PopulateFilters();
                        ApplyVoiceFilters();
                        VoicesListView.SelectedIndex = 0;
                    }
                }
                else
                {
                    CustomMessageBox.Show($"Không thể tải danh sách giọng nói. Lỗi: {response.ReasonPhrase}", "Lỗi Server", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Không thể kết nối đến server TTS local. Vui lòng đảm bảo server đang chạy.\nChi tiết: {ex.Message}", "Lỗi Kết Nối", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void PopulateFilters()
        {
            if (allVoices == null) return;
            var languageItems = new List<LanguageFilterItem> { new LanguageFilterItem { DisplayName = "Tất cả ngôn ngữ", LocaleCode = "all" } };
            var uniqueLocales = allVoices.Select(v => v.Locale).Distinct().ToList();
            foreach (var locale in uniqueLocales)
            {
                try
                {
                    string regionCode = locale.Split('-').Last();
                    var regionInfo = new System.Globalization.RegionInfo(regionCode);
                    languageItems.Add(new LanguageFilterItem { DisplayName = regionInfo.EnglishName, LocaleCode = locale });
                }
                catch (ArgumentException) { languageItems.Add(new LanguageFilterItem { DisplayName = locale, LocaleCode = locale }); }
            }
            var sortedLanguageItems = languageItems.Skip(1).OrderBy(item => item.DisplayName).ToList();
            sortedLanguageItems.Insert(0, languageItems[0]);
            LanguageFilterComboBox.ItemsSource = sortedLanguageItems;
            LanguageFilterComboBox.DisplayMemberPath = "DisplayName";
            LanguageFilterComboBox.SelectedIndex = 0;
            var genders = allVoices.Select(v => v.Gender).Distinct().OrderBy(g => g).ToList();
            genders.Insert(0, "Tất cả giới tính");
            GenderFilterComboBox.ItemsSource = genders;
            GenderFilterComboBox.SelectedIndex = 0;
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { ApplyVoiceFilters(); }

        private void ApplyVoiceFilters()
        {
            if (allVoices == null) return;
            IEnumerable<FullVoiceInfo> filteredVoices = allVoices;
            if (LanguageFilterComboBox.SelectedIndex > 0)
            {
                var selectedItem = LanguageFilterComboBox.SelectedItem as LanguageFilterItem;
                if (selectedItem != null) { filteredVoices = filteredVoices.Where(v => v.Locale == selectedItem.LocaleCode); }
            }
            if (GenderFilterComboBox.SelectedIndex > 0)
            {
                string selectedGender = GenderFilterComboBox.SelectedItem as string;
                filteredVoices = filteredVoices.Where(v => v.Gender == selectedGender);
            }
            VoicesListView.ItemsSource = filteredVoices.ToList();
            if (VoicesListView.Items.Count > 0) { VoicesListView.SelectedIndex = 0; }
        }
        #endregion

        #region Single Text-to-Speech
        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (VoicesListView.SelectedItem == null)
            {
                CustomMessageBox.Show("Vui lòng chọn một giọng nói.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TextInput.Text))
            {
                CustomMessageBox.Show("Vui lòng nhập văn bản cần chuyển đổi.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateButton.IsEnabled = false;
            GenerateButton.Content = "Đang xử lý...";

            // Dọn dẹp trạng thái cũ
            playbackTimer.Stop();
            if (player.Source != null)
            {
                player.Stop();
                player.Close();
            }
            if (!string.IsNullOrEmpty(currentTempAudioPath) && File.Exists(currentTempAudioPath))
            {
                try { File.Delete(currentTempAudioPath); } catch (Exception ex) { Debug.WriteLine($"Lỗi xóa file tạm cũ: {ex.Message}"); }
            }
            PlaybackControlsPanel.Visibility = Visibility.Collapsed;
            isPlaying = false;
            TimelineSlider.Value = 0;
            TimeProgressText.Text = "00:00";
            TimeTotalText.Text = "00:00";

            try
            {
                var selectedVoice = VoicesListView.SelectedItem as FullVoiceInfo;
                var payload = new { model = "tts-1", input = TextInput.Text, voice = selectedVoice.Name, response_format = "mp3", speed = SpeedSlider.Value };
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{baseApiUrl}/v1/audio/speech", content);

                if (response.IsSuccessStatusCode)
                {
                    currentTempAudioPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".mp3");
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        using (var fileStream = new FileStream(currentTempAudioPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await stream.CopyToAsync(fileStream);
                        }
                    }

                    // Đăng ký sự kiện MediaOpened để lấy tổng thời lượng khi file sẵn sàng
                    player.MediaOpened += (s, args) =>
                    {
                        if (player.NaturalDuration.HasTimeSpan)
                        {
                            TimeSpan totalDuration = player.NaturalDuration.TimeSpan;
                            TimelineSlider.Maximum = totalDuration.TotalSeconds;
                            TimeTotalText.Text = FormatTimeSpan(totalDuration);
                        }
                    };

                    player.Open(new Uri(currentTempAudioPath));
                    PlayPauseButton.Content = "▶ Play";
                    PlaybackControlsPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    CustomMessageBox.Show($"Tạo giọng nói thất bại. Lỗi từ server:\n{errorContent}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi khi tạo giọng nói: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "Tạo Giọng Nói và Nghe Thử";
            }
        }
        #endregion

        #region SRT Batch Processing
        private void LoadSrtButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "SRT Files (*.srt)|*.srt|All files (*.*)|*.*", Title = "Chọn file phụ đề" };
            if (openFileDialog.ShowDialog() == true)
            {
                srtFilePath = openFileDialog.FileName;
                SrtFilePathText.Text = srtFilePath;
                try
                {
                    loadedSrtLines = ParseSrtFile(srtFilePath);
                    CustomMessageBox.Show($"Đã tải thành công {loadedSrtLines.Count} dòng phụ đề.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"Lỗi khi đọc file SRT: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    srtFilePath = null;
                    loadedSrtLines = null;
                    SrtFilePathText.Text = "Chưa chọn file nào.";
                }
            }
        }

        private void SelectOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục để lưu file âm thanh";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    outputFolderPath = dialog.SelectedPath;
                    OutputFolderPathText.Text = outputFolderPath;
                }
            }
        }

        private async void StartBatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (VoicesListView.SelectedItem == null) { CustomMessageBox.Show("Vui lòng chọn một giọng nói.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (loadedSrtLines == null || !loadedSrtLines.Any()) { CustomMessageBox.Show("Vui lòng tải lên một file SRT.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrEmpty(outputFolderPath)) { CustomMessageBox.Show("Vui lòng chọn thư mục lưu file.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(OutputFileNamePrefix.Text)) { CustomMessageBox.Show("Vui lòng nhập tên file đầu ra.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // Thiết lập UI cho trạng thái đang chạy
            StartBatchButton.IsEnabled = false;
            CancelBatchButton.Visibility = Visibility.Visible;
            int totalLines = loadedSrtLines.Count;
            BatchProgressBar.Value = 0;
            BatchProgressBar.Maximum = totalLines;
            BatchProgressStatusText.Text = $"Đã xử lý 0 / {totalLines}";

            string selectedVoiceName = (VoicesListView.SelectedItem as FullVoiceInfo).Name;
            double currentSpeed = SpeedSlider.Value;
            string outputPrefix = OutputFileNamePrefix.Text;
            int completedCount = 0;

            // Khởi tạo CancellationTokenSource
            batchCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = batchCancellationTokenSource.Token;

            try
            {
                var progress = new Progress<int>(processedCount =>
                {
                    BatchProgressBar.Value = processedCount;
                    BatchProgressStatusText.Text = $"Đã xử lý {processedCount} / {totalLines}";
                });

                using (var semaphore = new SemaphoreSlim(200, 200))
                {
                    var tasks = new List<Task>();
                    foreach (var srtLine in loadedSrtLines)
                    {
                        // Nếu có yêu cầu hủy, ném ra exception để dừng vòng lặp
                        cancellationToken.ThrowIfCancellationRequested();

                        await semaphore.WaitAsync(cancellationToken);

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var payload = new { model = "tts-1", input = srtLine.Text, voice = selectedVoiceName, response_format = "mp3", speed = currentSpeed };
                                string jsonPayload = JsonConvert.SerializeObject(payload);
                                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                                // Truyền cancellationToken vào request
                                var response = await httpClient.PostAsync($"{baseApiUrl}/v1/audio/speech", content, cancellationToken);

                                if (response.IsSuccessStatusCode)
                                {
                                    using (var audioStream = await response.Content.ReadAsStreamAsync())
                                    {
                                        string outputFilePath = Path.Combine(outputFolderPath, $"{outputPrefix}_{srtLine.Index}.mp3");
                                        using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                                        {
                                            await audioStream.CopyToAsync(fileStream);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            {
                                Debug.WriteLine($"Exception dòng {srtLine.Index}: {ex.Message}");
                            }
                            finally
                            {
                                int currentCompleted = Interlocked.Increment(ref completedCount);
                                (progress as IProgress<int>).Report(currentCompleted);
                                semaphore.Release();
                            }
                        }, cancellationToken));
                    }
                    await Task.WhenAll(tasks);
                }
                CustomMessageBox.Show("Xử lý hàng loạt hoàn tất!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                // Xử lý khi tác vụ bị hủy
                CustomMessageBox.Show("Quá trình xử lý hàng loạt đã được hủy.", "Đã hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
                BatchProgressStatusText.Text = "Đã hủy";
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Đã xảy ra lỗi nghiêm trọng trong quá trình xử lý hàng loạt: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Dọn dẹp và khôi phục UI
                StartBatchButton.IsEnabled = true;
                CancelBatchButton.Visibility = Visibility.Collapsed;
                batchCancellationTokenSource.Dispose();
                batchCancellationTokenSource = null;
            }
        }

        private void CancelBatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (batchCancellationTokenSource != null)
            {
                CancelBatchButton.IsEnabled = false; // Vô hiệu hóa nút hủy sau khi click
                CancelBatchButton.Content = "Đang hủy...";
                batchCancellationTokenSource.Cancel();
            }
        }
        #endregion

        #region Helpers & Data Models
        public List<SrtLine> ParseSrtFile(string filePath)
        {
            var lines = new List<SrtLine>();
            string srtContent = File.ReadAllText(filePath);
            var regex = new Regex(@"(\d+)\r?\n(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})\r?\n([\s\S]*?)(?=\r?\n\r?\n|\z)", RegexOptions.Multiline);
            var matches = regex.Matches(srtContent);
            foreach (Match match in matches)
            {
                lines.Add(new SrtLine
                {
                    Index = int.Parse(match.Groups[1].Value),
                    StartTime = match.Groups[2].Value,
                    EndTime = match.Groups[3].Value,
                    Text = match.Groups[4].Value.Trim().Replace(Environment.NewLine, " ")
                });
            }
            return lines;
        }

        public class FullVoiceInfo { [JsonProperty("name")] public string Name { get; set; } [JsonProperty("gender")] public string Gender { get; set; } [JsonProperty("language")] public string Locale { get; set; } }
        public class VoiceResponse { public List<FullVoiceInfo> voices { get; set; } }
        public class SrtLine { public int Index { get; set; } public string StartTime { get; set; } public string EndTime { get; set; } public string Text { get; set; } }
        public class LanguageFilterItem { public string DisplayName { get; set; } public string LocaleCode { get; set; } }
        public class BatchProgressReport { public int CompletedCount { get; set; } public int TotalCount { get; set; } }
        #endregion
    }
}