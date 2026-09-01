using subphimv1.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    // BƯỚC 1: Thêm Enum để định nghĩa mục đích của cửa sổ
    public enum ProgressWindowMode
    {
        AutoUpdater,
        Generic
    }

    public partial class DownloadProgressWindow : Window
    {
        private readonly ProgressWindowMode _mode;
        private readonly string _genericTitle;
        public DownloadProgressWindow() : this(ProgressWindowMode.AutoUpdater){ }
        public DownloadProgressWindow(ProgressWindowMode mode, string genericTitle = "Đang xử lý...")
        {
            InitializeComponent();
            _mode = mode;
            _genericTitle = genericTitle;
            this.Loaded += Window_Loaded;
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            switch (_mode)
            {
                case ProgressWindowMode.AutoUpdater:
                    await RunAutoUpdaterMode();
                    break;
                case ProgressWindowMode.Generic:
                    RunGenericMode();
                    break;
            }
        }

        private async Task RunAutoUpdaterMode()
        {
            var updater = App.Updater;
            this.DataContext = updater;

            TitleTextBlock.Text = $"Đang cập nhật lên phiên bản {updater.LatestVersion}...";
            CancelButton.Visibility = Visibility.Collapsed;
            updater.PropertyChanged += Updater_PropertyChanged;

            try
            {
                await updater.StartUpdateAsync();
                this.Close();
            }
            catch (Exception ex)
            {
                this.Close();
                CustomMessageBox.Show($"Cập nhật thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                updater.PropertyChanged -= Updater_PropertyChanged;
            }
        }

        private void Updater_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var updater = sender as UpdateService;
            if (updater == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Updater_PropertyChanged(sender, e));
                return;
            }

            if (e.PropertyName == nameof(UpdateService.DownloadProgress))
            {
                ProgressBar.Value = updater.DownloadProgress;
                PercentageTextBlock.Text = $"{updater.DownloadProgress:F1}%";
            }
            if (e.PropertyName == nameof(UpdateService.UpdateStatusText))
            {
                StatusTextBlock.Text = updater.UpdateStatusText;
            }
        }
        private void RunGenericMode()
        {
            Debug.WriteLine("[ProgressWindow] Running in Generic mode.");
            TitleTextBlock.Text = _genericTitle;
            CancelButton.Visibility = Visibility.Collapsed;
        }
        public void UpdateProgress(double percentage, string statusMessage, string percentageText = null)
        {
            if (_mode != ProgressWindowMode.Generic) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percentage, statusMessage, percentageText));
                return;
            }

            ProgressBar.Value = percentage;
            StatusTextBlock.Text = statusMessage;
            PercentageTextBlock.Text = percentageText ?? $"{percentage:F1}%";
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_mode == ProgressWindowMode.AutoUpdater)
            {
                App.Updater.PropertyChanged -= Updater_PropertyChanged;
            }
        }
    }
}