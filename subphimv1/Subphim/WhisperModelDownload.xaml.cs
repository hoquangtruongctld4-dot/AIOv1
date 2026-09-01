
using subphimv1.Services.Whisper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    public partial class WhisperModelDownloaderWindow : Window
    {
        private readonly IWhisperModelProvider _modelProvider;
        private readonly List<WhisperModelInfo> _models;
        private CancellationTokenSource _cts;

        public WhisperModelDownloaderWindow(IWhisperModelProvider provider)
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;

            _modelProvider = provider;
            _models = provider.GetModels();
            // ... (phần logic khởi tạo ModelComboBox không đổi)
            foreach (var model in _models)
            {
                string mainModelFile = "model.bin";
                string checkPath = Path.Combine(_modelProvider.ModelFolder, model.Folder ?? "", mainModelFile);

                if (File.Exists(checkPath))
                {
                    model.Name = "* " + model.Name;
                }
            }
            ModelComboBox.ItemsSource = _models;
            ModelComboBox.DisplayMemberPath = "DisplayName";
            if (_models.Any())
            {
                ModelComboBox.SelectedIndex = 0;
            }
        }

        // Phương thức để di chuyển cửa sổ vẫn cần thiết
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // Phương thức này được gọi bởi nút "Đóng" đã được khôi phục
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel(); // Hủy tác vụ nếu đang chạy
            this.Close();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is not WhisperModelInfo selectedModel)
            {
                return;
            }
            string mainModelFile = "model.bin";
            string checkPath = Path.Combine(_modelProvider.ModelFolder, selectedModel.Folder ?? "", mainModelFile);
            if (File.Exists(checkPath))
            {
                CustomMessageBox.Show($"Model '{selectedModel.Name.Replace("* ", "")}' dường như đã tồn tại trong thư mục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetUiState(false);
            StatusTextBlock.Text = "Đang chuẩn bị tải...";
            DownloadProgressBar.Value = 0;
            _cts = new CancellationTokenSource();

            try
            {
                _modelProvider.CreateModelFolder();
                string targetDirectory = Path.Combine(_modelProvider.ModelFolder, selectedModel.Folder ?? "");
                Directory.CreateDirectory(targetDirectory);
                for (int i = 0; i < selectedModel.Urls.Length; i++)
                {
                    string url = selectedModel.Urls[i];
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    string destinationPath = Path.Combine(targetDirectory, fileName);

                    StatusTextBlock.Text = $"Đang tải file {i + 1}/{selectedModel.Urls.Length}: {fileName}...";

                    var progress = new Progress<double>(p => DownloadProgressBar.Value = p);
                    bool downloaded = await DownloadFileAsync(url, destinationPath, progress, _cts.Token);

                    if (!downloaded)
                    {
                        StatusTextBlock.Text = $"Bỏ qua file không tồn tại: {fileName}";
                        await Task.Delay(500);
                    }
                }
                StatusTextBlock.Text = "Tải xuống hoàn tất!";
                this.Close();
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Đã hủy tải xuống.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Tải xuống thất bại.";
            }
            finally
            {
                SetUiState(true);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<double> progress, CancellationToken token)
        {
            // ... (phần logic không thay đổi)
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using (var client = new HttpClient(handler))
            {
                try
                {
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            return false;
                        }

                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength;

                        using (var contentStream = await response.Content.ReadAsStreamAsync(token))
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var totalBytesRead = 0L;
                            var buffer = new byte[8192];
                            int bytesRead;
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                                totalBytesRead += bytesRead;
                                if (totalBytes.HasValue)
                                {
                                    progress.Report((double)totalBytesRead / totalBytes.Value * 100);
                                }
                            }
                        }
                    }
                    return true;
                }
                catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return false;
                }
            }
        }

        private void SetUiState(bool isEnabled)
        {
            DownloadButton.IsEnabled = isEnabled;
            ModelComboBox.IsEnabled = isEnabled;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }
}