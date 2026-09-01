using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace subphimv1.Services
{
    public class UpdateService : INotifyPropertyChanged
    {
        private System.Timers.Timer _timer;
        private readonly HttpClient _httpClient; 

        #region Public Properties for Binding
        private string _latestVersion;
        public string LatestVersion { get => _latestVersion; set { _latestVersion = value; OnPropertyChanged(nameof(LatestVersion)); } }

        private string _releaseNotes = "Vui lòng đăng nhập để xem thông tin cập nhật.";
        public string ReleaseNotes { get => _releaseNotes; set { _releaseNotes = value; OnPropertyChanged(nameof(ReleaseNotes)); } }

        private string _downloadUrl;
        public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(nameof(DownloadUrl)); } }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable { get => _isUpdateAvailable; set { _isUpdateAvailable = value; OnPropertyChanged(nameof(IsUpdateAvailable)); } }

        private bool _isUpdating;
        public bool IsUpdating { get => _isUpdating; set { _isUpdating = value; OnPropertyChanged(nameof(IsUpdating)); } }

        private double _downloadProgress;
        public double DownloadProgress { get => _downloadProgress; set { _downloadProgress = value; OnPropertyChanged(nameof(DownloadProgress)); } }

        private string _updateStatusText = "Tải về và Cập nhật";
        public string UpdateStatusText { get => _updateStatusText; set { _updateStatusText = value; OnPropertyChanged(nameof(UpdateStatusText)); } }
        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        public UpdateService()
        {
            _httpClient = new HttpClient();
        }

        public void Start()
        {
            if (_timer != null && _timer.Enabled)
            {
                Debug.WriteLine("[UpdateService] Timer is already running. Start request ignored.");
                return;
            }
            Debug.WriteLine("[UpdateService] Starting update check service.");

            // === THAY ĐỔI: Đặt timer thành 5 phút ===
            _timer = new System.Timers.Timer(5 * 60 * 1000); // 5 phút
            _timer.Elapsed += async (s, e) => await CheckForUpdateAsync();
            _timer.AutoReset = true;

            // Chạy kiểm tra ngay lập tức mà không cần đợi timer tick lần đầu
            Task.Run(CheckForUpdateAsync);

            _timer.Enabled = true;
        }
        public void Stop()
        {
            if (_timer != null)
            {
                Debug.WriteLine("[UpdateService] Stopping update check service.");
                _timer.Enabled = false;
                _timer.Dispose();
                _timer = null;
            }
            // Reset lại trạng thái UI
            IsUpdateAvailable = false;
        }

        public async Task CheckForUpdateAsync()
        {
            if (IsUpdating) return;

            bool isLoggedIn = false;
            Application.Current?.Dispatcher.Invoke(() => isLoggedIn = App.User.IsLoggedIn);
            if (!isLoggedIn)
            {
                IsUpdateAvailable = false;
                ReleaseNotes = "Vui lòng đăng nhập để xem thông tin cập nhật.";
                return;
            }

            var (success, updateInfo) = await ApiService.CheckForUpdateAsync();

            if (!success || updateInfo == null)
            {
                Debug.WriteLine("[UpdateService] Failed to get update info from server.");
                return;
            }

            try
            {
                ReleaseNotes = updateInfo.ReleaseNotes;
                DownloadUrl = updateInfo.DownloadUrl;
                LatestVersion = updateInfo.LatestVersion;

                string currentVersionStr = Assembly.GetExecutingAssembly().GetName().Version.ToString(3); // Lấy dạng X.Y.Z

                IsUpdateAvailable = IsNewerVersion(updateInfo.LatestVersion, currentVersionStr) && !string.IsNullOrEmpty(DownloadUrl);
                Debug.WriteLine($"[UpdateService] Check complete. Current: {currentVersionStr}, Latest: {updateInfo.LatestVersion}, Update Available: {IsUpdateAvailable}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error processing update info: {ex.Message}");
                IsUpdateAvailable = false;
            }
        }

        public async Task StartUpdateAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl) || IsUpdating) return;

            IsUpdating = true;
            UpdateStatusText = "Đang tải...";
            DownloadProgress = 0;

            try
            {
                string tempZipPath = Path.Combine(Path.GetTempPath(), "update.zip");

                using (var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var totalBytesRead = 0L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (totalBytes != -1)
                            {
                                DownloadProgress = (double)totalBytesRead / totalBytes * 100;
                            }
                        }
                    }
                }

                UpdateStatusText = "Đang giải nén...";
                DownloadProgress = 100;

                string appPath = Process.GetCurrentProcess().MainModule.FileName;
                string appDir = Path.GetDirectoryName(appPath);
                string scriptPath = Path.Combine(Path.GetTempPath(), "update_script.ps1");

                string scriptContent = $@"
# Chờ ứng dụng chính tắt hoàn toàn
Write-Host 'Dang cho ung dung dong...'
Start-Sleep -Seconds 3
# Giải nén file update, ghi đè lên các file cũ
Write-Host 'Dang giai nen ban cap nhat...'
Expand-Archive -Path ""{tempZipPath}"" -DestinationPath ""{appDir}"" -Force
# Chạy lại ứng dụng từ file exe mới
Write-Host 'Khoi dong lai ung dung...'
Start-Process -FilePath ""{appPath}""
# Dọn dẹp file zip và chính kịch bản này
Write-Host 'Don dep...'
Remove-Item -Path ""{tempZipPath}"" -Force
Remove-Item -Path $MyInvocation.MyCommand.Path -Force
";
                File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.UTF8);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                if (ex is Win32Exception winEx && winEx.NativeErrorCode == 1223)
                {
                    UpdateStatusText = "Đã hủy cập nhật";
                }
                else
                {
                    UpdateStatusText = "Lỗi! Thử lại?";
                }
                IsUpdating = false;
                OnPropertyChanged(nameof(UpdateStatusText));
                OnPropertyChanged(nameof(IsUpdating));
            }
        }
        private bool IsNewerVersion(string latestStr, string currentStr)
        {
            try
            {
                var latestVer = new Version(latestStr.TrimStart('v'));
                var currentVer = new Version(currentStr);
                return latestVer > currentVer;
            }
            catch
            {
                return false;
            }
        }
    }
}