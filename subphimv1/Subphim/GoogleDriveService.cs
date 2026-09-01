// FILE: GoogleDriveService.cs
// THAY THẾ TOÀN BỘ FILE NÀY

using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Subphim
{
    public class GoogleDriveService
    {
        private static readonly string[] Scopes = { DriveService.Scope.Drive };
        private static readonly string ApplicationName = "subphimv1 Drive API";

        // Các biến thành viên để lưu thông tin của từng tài khoản
        private readonly string _credentialsPath;
        private readonly string _tokenDirectoryPath;
        private readonly string _accountLabel;
        private DriveService _service;
        private readonly SemaphoreSlim _authSemaphore = new SemaphoreSlim(1, 1);

        private const int MAX_RETRIES = 10;
        private const int INITIAL_DELAY_MS = 1000;

        public Action<string, bool> LogMessage { get; set; }

        // Constructor mới nhận thông tin của từng tài khoản
        public GoogleDriveService(string credentialsPath, string tokenDirectoryPath, string accountLabel)
        {
            _credentialsPath = credentialsPath;
            _tokenDirectoryPath = tokenDirectoryPath;
            _accountLabel = accountLabel;
        }

        private void InternalLog(string message, bool isError = false)
        {
            // Thêm nhãn tài khoản vào log để debug
            LogMessage?.Invoke($"[GDRIVE][{_accountLabel}] {message}", isError);
        }

        private string GetMimeTypeForFile(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            var mimeTypes = new Dictionary<string, string>
            {
                {".jpg", "image/jpeg"}, {".jpeg", "image/jpeg"}, {".png", "image/png"},
                {".gif", "image/gif"}, {".bmp", "image/bmp"}, {".tiff", "image/tiff"},
                {".webp", "image/webp"},
            };
            return mimeTypes.TryGetValue(extension, out string mimeType) ? mimeType : "application/octet-stream";
        }

        private bool IsTransientGoogleApiException(Exception ex)
        {
            if (ex is GoogleApiException gae)
            {
                System.Net.HttpStatusCode? statusCode = gae.HttpStatusCode;

                if (gae.Error?.Errors != null)
                {
                    foreach (var error in gae.Error.Errors)
                    {
                        string reason = error.Reason?.ToLowerInvariant();
                        if (reason == "ratelimitexceeded" || reason == "userratelimitexceeded" || reason == "quotaexceeded" || reason == "backenderror")
                            return true;
                    }
                }
                if (statusCode.HasValue)
                {
                    int code = (int)statusCode.Value;
                    return code == 429 || code >= 500;
                }
            }
            if (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested) return true; // Timeout
            if (ex is HttpRequestException) return true; // Network issue

            return false;
        }

        private async Task<T> ExecuteWithRetriesAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    attempt++;
                    return await operation();
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) throw;

                    if (attempt >= MAX_RETRIES || !IsTransientGoogleApiException(ex))
                    {
                        InternalLog($"Thao tác thất bại sau {attempt} lần thử hoặc do lỗi không thể phục hồi: {ex.Message}", true);
                        throw;
                    }
                    int delay = Math.Min(INITIAL_DELAY_MS * (int)Math.Pow(2, attempt - 1), 30000);
                    InternalLog($"Lần thử {attempt} thất bại, thử lại sau {delay / 1000}s... Lỗi: {ex.Message}", true);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        public async Task<(UserCredential credential, bool isNewToken)> GetCredentialsAsync()
        {
            if (!File.Exists(_credentialsPath))
            {
                InternalLog($"Lỗi: Không tìm thấy file credentials tại '{_credentialsPath}'", true);
                return (null, false);
            }
            // Mỗi tài khoản sẽ có file token riêng trong thư mục của nó
            string tokenFilePath = Path.Combine(_tokenDirectoryPath, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user");
            bool tokenExistsBefore = File.Exists(tokenFilePath);

            UserCredential credential;
            using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(_tokenDirectoryPath, true));
            }

            bool tokenExistsAfter = File.Exists(tokenFilePath);
            bool isNewToken = !tokenExistsBefore && tokenExistsAfter;

            if (isNewToken)
            {
                InternalLog("Phát hiện token mới được tạo. Sẽ làm mới Drive Service.");
            }

            return (credential, isNewToken);
        }

        public async Task<DriveService> GetServiceAsync()
        {
            await _authSemaphore.WaitAsync();
            try
            {
                var (credential, isNewToken) = await GetCredentialsAsync();
                if (isNewToken && _service != null)
                {
                    _service = null;
                }
                if (_service != null) return _service;

                if (credential == null)
                {
                    return null;
                }
                _service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
            }
            finally
            {
                _authSemaphore.Release();
            }
            return _service;
        }

        public async Task<(string fileId, string error)> UploadAndConvertImageAsync(string imagePath, string imageName, string googleFolderId, CancellationToken cancellationToken, string ocrLanguage)
        {
            try
            {
                var service = await GetServiceAsync();
                if (service == null) return (null, "Không thể kết nối tới Google Drive Service.");

                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = imageName,
                    MimeType = "application/vnd.google-apps.document",
                    Parents = new[] { googleFolderId }
                };

                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    string sourceMimeType = GetMimeTypeForFile(imageName);
                    var request = service.Files.Create(fileMetadata, stream, sourceMimeType);
                    request.Fields = "id";
                    if (!string.IsNullOrWhiteSpace(ocrLanguage))
                        request.OcrLanguage = ocrLanguage;

                    var uploadResult = await ExecuteWithRetriesAsync(
                        async () => await request.UploadAsync(cancellationToken),
                        cancellationToken);

                    if (uploadResult.Status == Google.Apis.Upload.UploadStatus.Completed)
                        return (request.ResponseBody?.Id, null);

                    return (null, $"Tải lên thất bại sau khi thử lại: {uploadResult.Exception?.Message ?? "Lỗi không xác định"}");
                }
            }
            catch (OperationCanceledException) { return (null, "Thao tác tải lên/chuyển đổi đã bị người dùng hủy."); }
            catch (Exception ex) { return (null, $"[LỖI_TẢI_LÊN_VĨNH_VIỄN] {ex.Message}"); }
        }

        public async Task<(bool success, string error)> DownloadTextFileAsync(string fileId, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                var service = await GetServiceAsync();
                if (service == null) return (false, "Không thể kết nối tới Google Drive Service.");

                var request = service.Files.Export(fileId, "text/plain");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    var downloadResult = await ExecuteWithRetriesAsync(
                        async () => await request.DownloadAsync(stream, cancellationToken),
                        cancellationToken);

                    if (downloadResult.Status == DownloadStatus.Completed)
                    {
                        return (true, null);
                    }
                    return (false, $"Tải xuống thất bại sau khi thử lại: {downloadResult.Exception?.Message ?? "Lỗi không xác định"}");
                }
            }
            catch (OperationCanceledException) { return (false, "Thao tác tải xuống đã bị người dùng hủy."); }
            catch (Exception ex) { return (false, $"[LỖI_TẢI_XUỐNG_VĨNH_VIỄN] {ex.Message}"); }
        }

        public async Task<(bool success, string error)> DeleteFileAsync(string fileId, CancellationToken cancellationToken)
        {
            try
            {
                var service = await GetServiceAsync();
                if (service == null) return (false, "Không thể kết nối tới Google Drive Service.");

                await ExecuteWithRetriesAsync(
                    async () => {
                        await service.Files.Delete(fileId).ExecuteAsync(cancellationToken);
                        return true;
                    },
                    cancellationToken);
                return (true, null);
            }
            catch (OperationCanceledException) { return (false, "Thao tác xóa đã bị người dùng hủy."); }
            catch (Exception ex) { return (false, $"[LỖI_XÓA_VĨNH_VIỄN] {ex.Message}"); }
        }
    }
}