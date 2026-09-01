using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using subphimv1.Services;
using System.Threading.Tasks;

namespace subphimv1
{
    public class ResolutionOption
    {
        public string Name { get; set; }
        public System.Windows.Size Size { get; set; }
        public override string ToString() => Name;
    }

    public class VideoExportSettings
    {
        public string OutputPath { get; set; } 
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public string VideoCodec { get; set; }
        public string Preset { get; set; }
        public int Crf { get; set; }
        public string Tune { get; set; }
        public string AudioCodec { get; set; }
        public string AudioBitrate { get; set; }
        public bool ForceStereo { get; set; }
        public string PixelFormat { get; set; }
        public TimeSpan Duration { get; set; }
        public string GpuAcceleration { get; set; } 
        public bool IsThreadLimitEnabled { get; set; }
        public int ThreadLimit { get; set; }
    }

    public partial class GenerateVideoWindow : Window
    {
        public event Action<VideoExportSettings> OnGenerateClicked;
        public VideoExportSettings ResultSettings { get; private set; }
        private bool _isExporting = false;
        private TimeSpan _videoDuration;
        private List<GpuOption> _availableGpus;
        private bool _isGpuSelectionChanging = false;
        private string _lastGpuToApply;

        public GenerateVideoWindow(string defaultProjectName, string defaultOutputPath, int videoWidth, int videoHeight, TimeSpan duration,
             string lastGpu, bool lastThreadLimitEnabled, int lastThreadLimit)
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
            _videoDuration = duration;
            FileNameTextBox.Text = defaultProjectName;
            if (!string.IsNullOrEmpty(defaultOutputPath) && Directory.Exists(Path.GetDirectoryName(defaultOutputPath)))
            {
                OutputPathTextBox.Text = Path.Combine(
                    Path.GetDirectoryName(defaultOutputPath),
                    $"{FileNameTextBox.Text}.mp4"
                );
            }
            else
            {
                OutputPathTextBox.Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    $"{FileNameTextBox.Text}.mp4"
                );
            }

            InitializeComboBoxes(videoWidth, videoHeight);
            InitializePerformanceSettings();
            UpdateControlsForCodec();
            _lastGpuToApply = lastGpu;
            EnableThreadLimitCheckBox.IsChecked = lastThreadLimitEnabled;
            ThreadLimitPanel.IsEnabled = lastThreadLimitEnabled;
            if (lastThreadLimit > 0 && lastThreadLimit <= ThreadSlider.Maximum)
            {
                ThreadSlider.Value = lastThreadLimit;
            }
            FileNameTextBox.TextChanged += (s, e) => UpdateOutputPath();
            _ = LoadGpuOptionsAsync();
        }
        private async Task LoadGpuOptionsAsync()
        {
            var detectedGpus = await FfmpegGpuDetector.GetAvailableGpusAsync();
            _availableGpus = new List<GpuOption>();
            _availableGpus.Add(new GpuOption { DisplayName = "Tự động (Đề xuất)", FfmpegValue = "auto" });
            if (detectedGpus.Count > 1) 
            {
                _availableGpus.AddRange(detectedGpus);
            }
            else 
            {
                _availableGpus.Add(detectedGpus.First(g => g.FfmpegValue == "none"));
            }

            GpuComboBox.ItemsSource = _availableGpus;
            var savedOption = _availableGpus.FirstOrDefault(g => g.FfmpegValue == _lastGpuToApply);
            GpuComboBox.SelectedItem = savedOption ?? _availableGpus.First(g => g.FfmpegValue == "auto");
        }
        private void InitializePerformanceSettings()
        {
            int coreCount = Environment.ProcessorCount;
            ThreadSlider.Maximum = coreCount;
            int defaultThreads = Math.Max(1, (int)Math.Round(coreCount * 0.3));
            ThreadSlider.Value = defaultThreads;

            EnableThreadLimitCheckBox.IsChecked = false; 
            ThreadLimitPanel.IsEnabled = false;
        }

        private void GpuComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GpuComboBox.SelectedItem is GpuOption selectedGpuOption)
            {
                _isGpuSelectionChanging = true;

                GpuOption effectiveGpu = selectedGpuOption;

                if (selectedGpuOption.FfmpegValue == "auto")
                {
                    effectiveGpu = FfmpegGpuDetector.GetBestAvailableGpu(_availableGpus);
                    if (effectiveGpu == null)
                    {
                        effectiveGpu = new GpuOption { FfmpegValue = "none", RecommendedCodec = "libx264" };
                    }
                }

                if (effectiveGpu.FfmpegValue != "none")
                {
                    var matchingCodec = VideoCodecComboBox.Items.OfType<string>()
                        .FirstOrDefault(c => c.StartsWith(effectiveGpu.RecommendedCodec));

                    if (matchingCodec != null)
                    {
                        VideoCodecComboBox.SelectedItem = matchingCodec;
                    }
                    VideoCodecComboBox.IsEnabled = false;
                }
                else
                {
                    VideoCodecComboBox.IsEnabled = true;
                    VideoCodecComboBox.SelectedItem = VideoCodecComboBox.Items.OfType<string>().FirstOrDefault(c => c.StartsWith("libx264"));
                }
                UpdateControlsForCodec();
                _isGpuSelectionChanging = false;
            }
        }
        private void EnableThreadLimitCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ThreadLimitPanel != null)
            {
                ThreadLimitPanel.IsEnabled = EnableThreadLimitCheckBox.IsChecked == true;
            }
        }
        private void UpdateOutputPath()
        {
            try
            {
                string currentDirectory = Path.GetDirectoryName(OutputPathTextBox.Text);
                string extension = Path.GetExtension(OutputPathTextBox.Text);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".mp4"; 
                }
                string newFileName = $"{FileNameTextBox.Text}{extension}";
                OutputPathTextBox.Text = Path.Combine(currentDirectory, newFileName);
            }
            catch (Exception ex)
            {

            }
        }
        private void InitializeComboBoxes(int currentWidth, int currentHeight)
        {
            var resolutions = new List<ResolutionOption>();
            double originalAspectRatio = (double)currentWidth / currentHeight;

            var standardResolutions = new Dictionary<string, int>
    {
        { "480p", 480 },
        { "720p (HD)", 720 },
        { "1080p (Full HD)", 1080 },
        { "2K (QHD)", 1440 },
        { "4K (UHD)", 2160 }
    };

            // Xác định video là ngang, dọc hay vuông
            if (originalAspectRatio > 1.1) // Video ngang (ví dụ 16:9)
            {
                foreach (var res in standardResolutions)
                {
                    int height = res.Value;
                    int width = (int)Math.Round(height * originalAspectRatio);
                    if (width % 2 != 0) width++; // Đảm bảo chiều rộng là số chẵn
                    resolutions.Add(new ResolutionOption { Name = $"{res.Key} ({width}x{height})", Size = new System.Windows.Size(width, height) });
                }
            }
            else if (originalAspectRatio < 0.9) // Video dọc (ví dụ 9:16)
            {
                foreach (var res in standardResolutions)
                {
                    int width = res.Value;
                    int height = (int)Math.Round(width / originalAspectRatio);
                    if (height % 2 != 0) height++; // Đảm bảo chiều cao là số chẵn
                    resolutions.Add(new ResolutionOption { Name = $"{res.Key} ({width}x{height})", Size = new System.Windows.Size(width, height) });
                }
            }
            else // Video vuông (ví dụ 1:1)
            {
                foreach (var res in standardResolutions)
                {
                    int size = res.Value;
                    resolutions.Add(new ResolutionOption { Name = $"{res.Key} ({size}x{size})", Size = new System.Windows.Size(size, size) });
                }
            }
            resolutions.Insert(0, new ResolutionOption { Name = $"Giữ nguyên ({currentWidth}x{currentHeight})", Size = new System.Windows.Size(currentWidth, currentHeight) });
            resolutions.Add(new ResolutionOption { Name = "Tùy chỉnh", Size = new System.Windows.Size(currentWidth, currentHeight) });
            ResolutionComboBox.ItemsSource = resolutions;
            var matchingRes = resolutions.FirstOrDefault(r => r.Size.Width == currentWidth && r.Size.Height == currentHeight);
            ResolutionComboBox.SelectedItem = matchingRes ?? resolutions.First();

            VideoCodecComboBox.ItemsSource = new List<string> { "libx264 (H.264)", "libx265 (H.265)", "h264_nvenc (NVIDIA)", "hevc_nvenc (NVIDIA)", "libvpx-vp9 (WebM)", "prores_ks (Apple ProRes)" };
            VideoCodecComboBox.SelectedIndex = 0;
            AudioCodecComboBox.ItemsSource = new List<string> { "copy", "aac", "mp3" };
            AudioCodecComboBox.SelectedIndex = 1;
            AudioBitrateComboBox.ItemsSource = new List<string> { "96k", "128k", "192k", "256k", "320k" };
            AudioBitrateComboBox.SelectedIndex = 2;
            PixelFormatComboBox.ItemsSource = new List<string> { "(Mặc định)", "yuv420p (8-bit 4:2:0)", "yuv422p (8-bit 4:2:2)", "yuv444p (8-bit 4:4:4)", "yuv420p10le (10-bit 4:2:0)" };
            PixelFormatComboBox.SelectedIndex = 0;
        }

        #region Các phương thức quản lý UI
        private void SwitchToProgressView(VideoExportSettings settings)
        {
            _isExporting = true;
            TitleTextBlock.Text = "Đang xuất...";
            SettingsGrid.Visibility = Visibility.Collapsed;
            ProgressGrid.Visibility = Visibility.Visible;

            GenerateButton.IsEnabled = false;
            CancelButton.Content = "Hủy Bỏ";
            ProgressFileNameTextBlock.Text = Path.GetFileName(settings.OutputPath);
            ProgressDurationTextBlock.Text = settings.Duration.ToString(@"hh\:mm\:ss");
            ProgressResolutionTextBlock.Text = $"{settings.ResolutionWidth}x{settings.ResolutionHeight}";
            ProgressVideoCodecTextBlock.Text = settings.VideoCodec;
            ProgressAudioCodecTextBlock.Text = settings.AudioCodec;
        }

        public void UpdateProgress(int percentage, string status)
        {
            Dispatcher.Invoke(() => {
                MainProgressBar.Value = percentage;
                PercentageTextBlock.Text = $"{percentage}%";
                StatusTextBlock.Text = status;
            });
        }

        public void MarkAsComplete(bool success)
        {
            Dispatcher.Invoke(() => {
                CancelButton.Visibility = Visibility.Collapsed;
                GenerateButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                TitleTextBlock.Text = success ? "Xuất thành công!" : "Xuất thất bại!";
                _isExporting = false;
            });
        }
        #endregion

        #region Các trình xử lý sự kiện
        private void BrowseOutputPathButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Chọn nơi lưu video",
                Filter = "MP4 Video (*.mp4)|*.mp4|MKV Video (*.mkv)|*.mkv|All Files (*.*)|*.*",
                FileName = $"{FileNameTextBox.Text}.mp4",
                InitialDirectory = Path.GetDirectoryName(OutputPathTextBox.Text)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                OutputPathTextBox.Text = saveFileDialog.FileName;
                FileNameTextBox.Text = Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
            }
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedResolution = ResolutionComboBox.SelectedItem as ResolutionOption;
            if (selectedResolution == null)
            {
                CustomMessageBox.Show("Vui lòng chọn độ phân giải.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int width = (int)selectedResolution.Size.Width;
            int height = (int)selectedResolution.Size.Height;

            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            string outputPath = Path.Combine(
                Path.GetDirectoryName(OutputPathTextBox.Text),
                $"{FileNameTextBox.Text}{Path.GetExtension(OutputPathTextBox.Text)}"
            );

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                CustomMessageBox.Show("Đường dẫn xuất không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var selectedGpu = GpuComboBox.SelectedItem as GpuOption;
            string selectedVideoCodec;
            if (selectedGpu != null && selectedGpu.FfmpegValue != "auto" && selectedGpu.FfmpegValue != "none")
            {
                selectedVideoCodec = selectedGpu.RecommendedCodec;
            }
            else
            {
                string selectedVideoCodecItem = VideoCodecComboBox.SelectedItem.ToString();
                selectedVideoCodec = selectedVideoCodecItem.Split(' ')[0];
            }
            string selectedTune = TuneComboBox.SelectedItem?.ToString();
            string selectedPixelFormat = null;
            if (PixelFormatComboBox.SelectedItem is string selectedPixelFormatItem && selectedPixelFormatItem != "(Mặc định)")
            {
                selectedPixelFormat = selectedPixelFormatItem.Split(' ')[0];
            }

            ResultSettings = new VideoExportSettings
            {
                OutputPath = outputPath,
                ResolutionWidth = width,
                ResolutionHeight = height,
                VideoCodec = selectedVideoCodec,
                Preset = PresetComboBox.SelectedItem?.ToString(),
                Crf = (int)CrfSlider.Value,
                Tune = (selectedTune == "(Không)" || selectedTune == "(Không có)") ? null : selectedTune,
                AudioCodec = AudioCodecComboBox.SelectedItem.ToString(),
                AudioBitrate = AudioBitrateComboBox.SelectedItem.ToString(),
                ForceStereo = StereoCheckBox.IsChecked == true,
                PixelFormat = selectedPixelFormat,
                Duration = _videoDuration,
                GpuAcceleration = selectedGpu?.FfmpegValue ?? "auto", 
                IsThreadLimitEnabled = EnableThreadLimitCheckBox.IsChecked == true,
                ThreadLimit = (int)ThreadSlider.Value
            };

            OnGenerateClicked?.Invoke(ResultSettings);
            SwitchToProgressView(ResultSettings);
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
            {
                StatusTextBlock.Text = "Đang yêu cầu hủy bỏ...";
                CancelButton.IsEnabled = false;
            }
            else
            {
                this.Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        #endregion

        #region Các hàm cập nhật UI động
        private void VideoCodecComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isGpuSelectionChanging) return;
            UpdateControlsForCodec();
        }
        private void UpdateControlsForCodec()
        {
            // BẢN MỚI CHO FFmpeg 8.x
            //
            // Mục tiêu:
            //  - NVENC (CUDA) dùng preset p1..p7 / lossless... thay vì preset CPU.
            //  - Hiển thị CQ (constant quality) thay vì CRF cho NVENC.
            //  - Giữ nguyên logic cho libx264, libx265, libvpx-vp9, prores_ks.
            //
            // Tham chiếu:
            //  - h264_nvenc / hevc_nvenc hỗ trợ preset p1..p7, lossless/losslesshp, và chất lượng điều chỉnh bằng -cq. :contentReference[oaicite:9]{index=9}
            //  - libx264/libx265 dùng -crf và preset ultrafast..veryslow (CPU).
            //
            // Lưu ý:
            //  - Chúng ta KHÔNG chuyển toàn bộ filter pipeline sang CUDA filters (scale_cuda, overlay_cuda,...),
            //    chỉ tối ưu encode cuối, nên UI ở đây chỉ điều khiển encoder + preset + cq/crf. :contentReference[oaicite:10]{index=10}

            if (VideoCodecComboBox.SelectedItem == null || CrfSlider == null)
                return;

            string selectedCodec = VideoCodecComboBox.SelectedItem.ToString().Split(' ')[0];

            var presets = new List<string>();
            var tunes = new List<string>();
            string defaultPreset = "medium";

            // Thiết lập mặc định cho nhãn/chế độ chất lượng
            CrfLabel.Content = "Chất lượng (CRF)";
            CrfSlider.Minimum = 0;
            CrfSlider.Maximum = 51;
            TuneLabel.Visibility = Visibility.Visible;
            TuneComboBox.Visibility = Visibility.Visible;
            CrfHintTextBlock.Visibility = Visibility.Visible;
            PresetComboBox.IsEnabled = true;
            TuneComboBox.IsEnabled = true;
            CrfSlider.IsEnabled = true;

            // 1. libx264 (CPU H.264 software)
            if (selectedCodec.Contains("libx264"))
            {
                presets.AddRange(new[]
                {
            "ultrafast","superfast","veryfast","faster","fast",
            "medium","slow","slower","veryslow"
        });

                tunes.AddRange(new[]
                {
            "(Không)",
            "film",
            "animation",
            "grain",
            "stillimage",
            "fastdecode",
            "zerolatency"
        });

                defaultPreset = "medium";
                CrfSlider.Value = 23;
                CrfHintTextBlock.Text = "(0-51, nhỏ hơn = chất lượng cao hơn)";
                CrfLabel.Content = "Chất lượng (CRF)";
            }
            // 2. libx265 (CPU H.265/HEVC software)
            else if (selectedCodec.Contains("libx265"))
            {
                presets.AddRange(new[]
                {
            "ultrafast","superfast","veryfast","faster","fast",
            "medium","slow","slower","veryslow"
        });

                tunes.AddRange(new[]
                {
            "(Không)",
            "grain",
            "zerolatency",
            "fastdecode",
            "psnr",
            "ssim"
        });

                defaultPreset = "medium";
                CrfSlider.Value = 28;
                CrfHintTextBlock.Text = "(0-51, nhỏ hơn = chất lượng cao hơn)";
                CrfLabel.Content = "Chất lượng (CRF)";
            }
            // 3. NVENC / CUDA (NVIDIA), gồm h264_nvenc và hevc_nvenc
            //
            // FFmpeg 8.x hỗ trợ preset p1..p7 (p1 = nhanh nhất, p7 = chậm nhất/đẹp nhất),
            // cùng các preset đặc biệt như "lossless" và "losslesshp". :contentReference[oaicite:11]{index=11}
            //
            // NVENC dùng -cq thay vì -crf cho chất lượng (constant quality). :contentReference[oaicite:12]{index=12}
            else if (selectedCodec.Contains("nvenc"))
            {
                // Danh sách preset phần cứng NVENC hiện đại
                // p1..p7: p1 rất nhanh (chất lượng thấp hơn), p7 chậm nhất (chất lượng cao nhất).
                // "lossless"/"losslesshp": xuất không mất chất lượng.
                presets.AddRange(new[]
                {
            "p1","p2","p3","p4","p5","p6","p7",
            "lossless","losslesshp"
        });

                // Các "tune" hợp lệ thường gặp: hq (quality), ll / ull (low-latency / ultra-low-latency),
                // lossless. Những tune này map vào nội bộ NVENC. :contentReference[oaicite:13]{index=13}
                tunes.AddRange(new[]
                {
            "(Không)",
            "hq",
            "ll",
            "ull",
            "lossless"
        });

                // preset mặc định đề xuất p4: cân bằng tốc độ/chất lượng tương đương kiểu "medium" nhưng theo NVENC
                defaultPreset = "p4";

                // Với NVENC ta đang điều khiển bằng CQ thay vì CRF:
                CrfLabel.Content = "Chất lượng (CQ)";
                CrfSlider.Minimum = 0;
                CrfSlider.Maximum = 51;
                CrfSlider.Value = 23;
                CrfHintTextBlock.Text = "(0-51, nhỏ hơn = chất lượng tốt hơn, bitrate cao hơn)";
            }
            // 4. libvpx-vp9 (CPU/WebM)
            else if (selectedCodec.Contains("libvpx-vp9"))
            {
                presets.Add("(Không có)");
                tunes.Add("(Không có)");
                defaultPreset = "(Không có)";

                // vp9 dùng -crf và -b:v 0 kiểu VBR constrained, nhưng UI của bạn chỉ có CRF slider
                PresetComboBox.IsEnabled = false;
                TuneComboBox.IsEnabled = false;

                CrfSlider.Maximum = 63;
                CrfSlider.Value = 31;
                CrfHintTextBlock.Text = "(0-63, nhỏ hơn = chất lượng cao hơn)";
                CrfLabel.Content = "Chất lượng (CRF)";
            }
            // 5. prores_ks (Apple ProRes mezzanine)
            else if (selectedCodec.Contains("prores"))
            {
                // ProRes không có CRF/CQ; preset ở đây là profile ProRes
                presets.AddRange(new[]
                {
            "proxy","lt","standard","hq","4444","4444xq"
        });

                tunes.Add("(Không có)");
                defaultPreset = "standard";

                TuneComboBox.IsEnabled = false;
                CrfSlider.IsEnabled = false;

                CrfLabel.Content = "Chất lượng";
                CrfHintTextBlock.Text = "(ProRes profile)";
            }

            // Cập nhật UI combobox preset/tune sau khi xây dựng danh sách
            PresetComboBox.ItemsSource = presets;
            if (presets.Contains(defaultPreset))
            {
                PresetComboBox.SelectedItem = defaultPreset;
            }
            else if (presets.Count > 0)
            {
                PresetComboBox.SelectedIndex = 0;
            }

            TuneComboBox.ItemsSource = tunes;
            if (tunes.Count > 0)
            {
                TuneComboBox.SelectedIndex = 0;
            }
        }

        private void AudioCodecComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioCodecComboBox.SelectedItem == null || AudioBitrateComboBox == null) return;
            bool isCopy = AudioCodecComboBox.SelectedItem.ToString() == "copy";
            AudioBitrateComboBox.IsEnabled = !isCopy;
            StereoCheckBox.IsEnabled = !isCopy;
        }
        // THÊM VÀO BÊN TRONG LỚP GenerateVideoWindow (GenerateVideo.xaml.cs)
        public GenerateVideoWindow()
            : this(
                defaultProjectName: "Export",
                defaultOutputPath: System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "Export.mp4"
                ),
                videoWidth: 1280,
                videoHeight: 720,
                duration: TimeSpan.Zero,
                lastGpu: "auto",
                lastThreadLimitEnabled: false,
                lastThreadLimit: Math.Max(1, (int)Math.Round(Environment.ProcessorCount * 0.3))
              )
        {
            // Không cần gì thêm. Constructor này chỉ chuyển tiếp sang constructor đầy đủ.
        }

        private void CrfSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }
        #endregion
    }
}