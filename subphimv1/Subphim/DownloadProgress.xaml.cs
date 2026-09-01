using subphimv1.Services.Whisper;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    public partial class EngineDownloadProgressWindow : Window
    {
        private CancellationTokenSource _cts;
        private readonly EngineInfo _engineInfo;
        public EngineDownloadProgressWindow(EngineInfo engineInfo)
        {
            InitializeComponent();
            _engineInfo = engineInfo;
            this.Owner = Application.Current.MainWindow;
            this.Loaded += EngineDownloadProgressWindow_Loaded;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private async void EngineDownloadProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await StartDownload();
        }
        private async Task StartDownload()
        {
            _cts = new CancellationTokenSource();
            TitleTextBlock.Text = $"{_engineInfo.DisplayName} ({_engineInfo.Size})";

            var progressPercentage = new Progress<double>(p => {
                DownloadProgressBar.Value = p;
                StatusTextBlock.Text = $"Vui lòng chờ... {p:F0}%";
            });
            var progressStatus = new Progress<string>(s => StatusTextBlock.Text = s);

            try
            {
                await WhisperEngineManager.DownloadAndInstallAsync(_engineInfo, progressPercentage, progressStatus, _cts.Token);
                this.DialogResult = true; 
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Đã hủy tải xuống.";
                this.DialogResult = false; 
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Tải engine thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                this.DialogResult = false; 
            }
            finally
            {
                if (this.IsLoaded)
                {
                    this.Close();
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (CancelButton.IsEnabled && _cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }
}