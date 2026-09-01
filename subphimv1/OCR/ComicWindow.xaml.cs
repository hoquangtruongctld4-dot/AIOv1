using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using subphimv1.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace subphimv1
{
    public partial class OcrComicWindow : Window
    {
        private static long _lastApiCallTicks = 0;
        private string _selectedImagePath = "";
        private readonly HttpClient _httpClient = new();
        private string _customTranslatePrompt = "";
        private CancellationTokenSource _cancellationTokenSource;
        private List<string> _apiKeys = new List<string>();
        private static long lastGlobalRequestTicks = 0;
        public class ApiException : Exception
        {
            public System.Net.HttpStatusCode StatusCode { get; }

            public ApiException(string message, System.Net.HttpStatusCode statusCode) : base(message)
            {
                this.StatusCode = statusCode;
                Debug.WriteLine($"ApiException được tạo: StatusCode={statusCode}, Message={message}");
            }
        }

        public OcrComicWindow()
        {
            InitializeComponent();
            LoadApiKeys();
        }

        #region Window Controls & Management
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _httpClient.Dispose();
        }
        #endregion

        #region API Key Management
        private string GetApiKeyFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "LauncherAIO");
            Directory.CreateDirectory(appFolderPath);
            return Path.Combine(appFolderPath, "gemini_ocr_keys.json");
        }
        private void LoadApiKeys()
        {
            try
            {
                string filePath = GetApiKeyFilePath();
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    _apiKeys = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch (Exception ex) { UpdateStatus($"Lỗi tải API keys: {ex.Message}", true); }
        }
        private void SaveApiKeys()
        {
            try
            {
                string filePath = GetApiKeyFilePath();
                string json = JsonSerializer.Serialize(_apiKeys, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex) { CustomMessageBox.Show($"Không thể tự động lưu API keys:\n{ex.Message}", "Lỗi Lưu File", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private ApiKeyManagerV2 GetApiKeyManagerV2()
        {
            if (!_apiKeys.Any())
            {
                CustomMessageBox.Show("Vui lòng vào 'Quản lý API Keys' để thêm ít nhất một API Key của Gemini.", "Thiếu API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return new ApiKeyManagerV2(_apiKeys);
        }
        #endregion

        #region UI Event Handlers
        private void ManageApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            var apiDialog = new ApiKeysDialog(_apiKeys) { Owner = this };
            if (apiDialog.ShowDialog() == true)
            {
                _apiKeys = apiDialog.ApiKeys;
                SaveApiKeys();
                (Application.Current as App)?.ShowNotification("Đã cập nhật danh sách API keys.", isError: false);
            }
        }
        private void SelectImageButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.webp", Title = "Chọn một hình ảnh" };
            if (openFileDialog.ShowDialog() == true)
            {
                _selectedImagePath = openFileDialog.FileName;
                try
                {
                    SelectedImage.Source = new BitmapImage(new Uri(_selectedImagePath));
                    UpdateStatus($"Đã chọn ảnh: {Path.GetFileName(_selectedImagePath)}");
                }
                catch (Exception ex) { CustomMessageBox.Show($"Không thể tải ảnh: {ex.Message}", "Lỗi Tải Ảnh", MessageBoxButton.OK, MessageBoxImage.Error); _selectedImagePath = ""; }
            }
        }
        private void CustomPromptButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomPromptDialog(_customTranslatePrompt) { Owner = this };
            if (dialog.ShowDialog() == true) { _customTranslatePrompt = dialog.PromptText; (Application.Current as App)?.ShowNotification("Đã lưu prompt dịch tùy chỉnh.", isError: false); }
        }
        private async void OCRAndTranslateButton_Click(object sender, RoutedEventArgs e)
        {
            var apiKeyManager = GetApiKeyManagerV2();
            if (apiKeyManager == null) return;

            if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
            {
                CustomMessageBox.Show("Vui lòng chọn hoặc dán một hình ảnh hợp lệ trước.", "Chưa Chọn Ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetButtonsEnabled(false);
            try
            {
                // === BƯỚC 1: Đọc tất cả các tùy chọn từ UI trước khi xử lý ===
                var options = new ProcessingOptions
                {
                    OcrModel = (OcrModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    TranslateModel = (TranslateModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    TargetLanguage = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    Genre = (GenreComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(), // Lấy thể loại từ ComboBox
                    CustomTranslatePrompt = _customTranslatePrompt
                };

                UpdateStatus("Bắt đầu OCR ảnh...");
                OcrResultTextBox.Text = "Đang quét ảnh...";

                // === BƯỚC 2: Xử lý OCR ===
                string apiKeyOcr = await apiKeyManager.AcquireKeyAsync(CancellationToken.None);

                // Lấy prompt OCR phù hợp cho 1 ảnh duy nhất
                string ocrPrompt = GetOcrPromptForBatch(options.Genre, 1);

                var requestParts = new List<object>
        {
            new { text = ocrPrompt },
            new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(File.ReadAllBytes(_selectedImagePath)) } }
        };
                string ocrText = await SendMultiPartRequestAsync(requestParts, apiKeyOcr, options);
                OcrResultTextBox.Text = ocrText;

                // === BƯỚC 3: Xử lý Dịch ===
                UpdateStatus("OCR hoàn tất, bắt đầu dịch...");
                TranslatedResultTextBox.Text = "Đang dịch...";

                string apiKeyTranslate = await apiKeyManager.AcquireKeyAsync(CancellationToken.None);
                string translatedText = await TranslateTextAsync(ocrText, apiKeyTranslate, options);
                TranslatedResultTextBox.Text = translatedText;

                UpdateStatus("Hoàn tất!");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Lỗi trong quá trình OCR và Dịch: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Đã xảy ra lỗi.", true);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }
        private async void OCRFolderButton_Click(object sender, RoutedEventArgs e) => await ProcessFolder(onlyOcr: true);
        private async void OCRAndTranslateAllButton_Click(object sender, RoutedEventArgs e) => await ProcessFolder(onlyOcr: false);
        #endregion

        #region Core Processing Logic

        private async Task ProcessFolder(bool onlyOcr)
        {
            var apiKeyManager = GetApiKeyManagerV2();
            if (apiKeyManager == null) return;
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "Chọn thư mục chứa ảnh" };
            if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;
            string selectedFolder = dialog.FileName;
            var imageFiles = Directory.GetFiles(selectedFolder, "*.*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
            if (!imageFiles.Any()) { CustomMessageBox.Show("Thư mục không có ảnh hợp lệ.", "Không Tìm Thấy Ảnh", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            SetButtonsEnabled(false);
            MainProgressBar.Visibility = Visibility.Visible;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // === ĐỌC TÙY CHỌN TỪ UI MỘT LẦN DUY NHẤT (Đã cập nhật) ===
            var options = new ProcessingOptions
            {
                OcrModel = (OcrModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                TranslateModel = (TranslateModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                TargetLanguage = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                Genre = (GenreComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(), // Lấy giá trị từ ComboBox mới
                CustomTranslatePrompt = _customTranslatePrompt
            };

            var jobQueue = new ConcurrentQueue<ImageProcessingJob>();
            var finalResults = new ConcurrentDictionary<string, (string OcrText, string TranslatedText)>();
            UpdateStatus("Đang phân loại và nhóm công việc...");

            // === LOGIC TẠO VIỆC (PRODUCER) ĐÃ CẬP NHẬT ===
            await Task.Run(() =>
            {
                var uncutImagesBuffer = new List<string>();

                // Quyết định việc cắt ảnh dựa trên thể loại truyện
                bool allowCutting = options.Genre == "Manhwa / Webtoon (Trái->Phải, Dài)";

                foreach (var imagePath in imageFiles)
                {
                    using var image = new Bitmap(imagePath);
                    // Chỉ cắt ảnh nếu thể loại là Manhwa/Webtoon VÀ ảnh đủ dài
                    bool needsCutting = allowCutting && image.Height > 6000;

                    if (needsCutting)
                    {
                        var cutJob = new ImageProcessingJob { Type = JobType.CutImageParts, OriginalImagePaths = { imagePath } };
                        int numParts = (int)Math.Ceiling(image.Height / 4000.0);
                        for (int i = 0; i < numParts; i++) { var rect = new Rectangle(0, i * 4000, image.Width, Math.Min(4000, image.Height - (i * 4000))); using var cropped = image.Clone(rect, image.PixelFormat); using var ms = new MemoryStream(); cropped.Save(ms, ImageFormat.Png); cutJob.ImageDatas.Add(ms.ToArray()); }
                        jobQueue.Enqueue(cutJob);
                    }
                    else
                    {
                        uncutImagesBuffer.Add(imagePath);
                        if (uncutImagesBuffer.Count == 2) { var batchJob = new ImageProcessingJob { Type = JobType.FullImageBatch, OriginalImagePaths = new List<string>(uncutImagesBuffer) }; foreach (var path in uncutImagesBuffer) { batchJob.ImageDatas.Add(File.ReadAllBytes(path)); } jobQueue.Enqueue(batchJob); uncutImagesBuffer.Clear(); }
                    }
                }
                if (uncutImagesBuffer.Any()) { var singleJob = new ImageProcessingJob { Type = JobType.FullImageBatch, OriginalImagePaths = new List<string>(uncutImagesBuffer) }; singleJob.ImageDatas.Add(File.ReadAllBytes(uncutImagesBuffer.First())); jobQueue.Enqueue(singleJob); }
            }, token);

            int totalJobs = jobQueue.Count;
            int jobsCompleted = 0;
            MainProgressBar.Value = 0;
            UpdateStatus($"Sẵn sàng thực thi {totalJobs} yêu cầu API...");
            Interlocked.Exchange(ref lastGlobalRequestTicks, 0);

            var workerTasks = new List<Task>();
            const int MaxRetries = 10;

            for (int i = 0; i < _apiKeys.Count; i++)
            {
                int workerId = i + 1;
                workerTasks.Add(Task.Run(async () =>
                {
                    Debug.WriteLine($"Worker {workerId} đã khởi động.");
                    while (jobQueue.TryDequeue(out var job))
                    {
                        if (token.IsCancellationRequested)
                        {
                            Debug.WriteLine($"Worker {workerId} nhận được yêu cầu hủy, dừng xử lý.");
                            break;
                        }

                        var jobIdentifier = string.Join(", ", job.OriginalImagePaths.Select(Path.GetFileName));
                        Debug.WriteLine($"Worker {workerId} bắt đầu xử lý job cho: {jobIdentifier}");

                        bool jobSucceeded = false;
                        // Vòng lặp thử lại cho mỗi job
                        for (int attempt = 1; attempt <= MaxRetries; attempt++)
                        {
                            if (token.IsCancellationRequested) break;

                            try
                            {
                                string apiKey = await apiKeyManager.AcquireKeyAsync(token);
                                Debug.WriteLine($"Worker {workerId} | Job: {jobIdentifier} | Lần thử {attempt}/{MaxRetries} đã nhận được API key.");

                                var requestParts = new List<object>();

                                if (job.Type == JobType.CutImageParts)
                                {
                                    string prompt = $"You are an expert OCR engine. The following {job.ImageDatas.Count} images are ordered parts of a single, long, top-to-bottom webtoon page. Process them in order from left-to-right, top-to-bottom. Stitch their content seamlessly and return only the complete dialogue text as if it were from one image.";
                                    requestParts.Add(new { text = prompt });
                                    job.ImageDatas.ForEach(data => requestParts.Add(new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(data) } }));
                                    string ocrResultText = await SendMultiPartRequestAsync(requestParts, apiKey, options);
                                    finalResults[job.OriginalImagePaths.First()] = (ocrResultText, null);
                                }
                                else // JobType.FullImageBatch
                                {
                                    string prompt = GetOcrPromptForBatch(options.Genre, job.ImageDatas.Count);
                                    requestParts.Add(new { text = prompt });
                                    job.ImageDatas.ForEach(data => requestParts.Add(new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(data) } }));
                                    string combinedOcrText = await SendMultiPartRequestAsync(requestParts, apiKey, options);
                                    string[] ocrResults = combinedOcrText.Split(new[] { "------" }, StringSplitOptions.None);
                                    for (int k = 0; k < job.OriginalImagePaths.Count; k++)
                                    {
                                        finalResults[job.OriginalImagePaths[k]] = (k < ocrResults.Length ? ocrResults[k].Trim() : "Lỗi: API không trả về kết quả cho ảnh này.", null);
                                    }
                                }

                                Debug.WriteLine($"Worker {workerId} | Job: {jobIdentifier} | Xử lý THÀNH CÔNG ở lần thử {attempt}.");
                                jobSucceeded = true;
                                break; // Thoát khỏi vòng lặp retry khi thành công
                            }
                            catch (ApiException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests || apiEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                            {
                                Debug.WriteLine($"!!! Worker {workerId} | Job: {jobIdentifier} | Gặp lỗi có thể thử lại: {apiEx.StatusCode}. Lần thử {attempt}/{MaxRetries}.");
                                if (attempt == MaxRetries)
                                {
                                    Debug.WriteLine($"!!! Worker {workerId} | Job: {jobIdentifier} | ĐÃ THỬ LẠI TỐI ĐA. Bỏ qua job này.");
                                    foreach (var path in job.OriginalImagePaths) { finalResults[path] = ($"Lỗi xử lý Job sau {MaxRetries} lần thử: {apiEx.StatusCode}", null); }
                                }
                                else
                                {
                                    // Chờ một chút trước khi thử lại với key mới
                                    await Task.Delay(1000 + (attempt * 500), token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                Debug.WriteLine($"Worker {workerId} bị hủy trong lúc xử lý job cho {jobIdentifier}.");
                                break; // Thoát khỏi vòng lặp retry
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"!!! LỖI NGHIÊM TRỌNG tại Worker {workerId} | Job: {jobIdentifier}: {ex.Message}");
                                foreach (var path in job.OriginalImagePaths) { finalResults[path] = ($"Lỗi không thể thử lại: {ex.Message}", null); }
                                break; // Thoát khỏi vòng lặp retry đối với lỗi nghiêm trọng
                            }
                        } // Kết thúc vòng lặp retry

                        // Cập nhật progress bar chỉ sau khi một job đã hoàn tất (thành công hoặc thất bại)
                        int currentCompleted = Interlocked.Increment(ref jobsCompleted);
                        Dispatcher.Invoke(() =>
                        {
                            MainProgressBar.Value = (double)currentCompleted / totalJobs * 100;
                            UpdateStatus($"Đã hoàn thành {currentCompleted}/{totalJobs} yêu cầu API...");
                        });
                    }
                    Debug.WriteLine($"Worker {workerId} đã hoàn thành tất cả các job trong hàng đợi.");
                }, token));
            }

            try
            {
                await Task.WhenAll(workerTasks);
                if (token.IsCancellationRequested) throw new OperationCanceledException();


                if (!onlyOcr)
                {
                    UpdateStatus("Đang dịch kết quả...");
                    Debug.WriteLine("Bắt đầu giai đoạn dịch thuật với concurrency được kiểm soát.");

                    // Tạo một Semaphore để giới hạn số lượng tác vụ dịch chạy song song.
                    // Số lượng tác vụ song song = số lượng API key bạn có.
                    int maxConcurrentTranslations = _apiKeys.Count > 0 ? _apiKeys.Count : 1;
                    using var translationSemaphore = new SemaphoreSlim(maxConcurrentTranslations, maxConcurrentTranslations);

                    Debug.WriteLine($"Đã tạo Semaphore cho việc dịch, cho phép tối đa {maxConcurrentTranslations} tác vụ chạy cùng lúc.");

                    var translationTasks = finalResults.Keys.Select(async imagePath =>
                    {
                        // Mỗi tác vụ phải "chờ" để lấy được một "vé" từ semaphore trước khi chạy.
                        await translationSemaphore.WaitAsync(token);
                        Debug.WriteLine($"Tác vụ dịch cho '{Path.GetFileName(imagePath)}' đã lấy được vé, bắt đầu thực thi.");

                        try
                        {
                            if (finalResults.TryGetValue(imagePath, out var result) && !string.IsNullOrWhiteSpace(result.OcrText) && !result.OcrText.StartsWith("Lỗi"))
                            {
                                bool translationSucceeded = false;
                                // Áp dụng lại logic retry tương tự như OCR để tăng độ ổn định
                                for (int attempt = 1; attempt <= 3; attempt++)
                                {
                                    if (token.IsCancellationRequested) break;
                                    try
                                    {
                                        // Lấy key từ manager, giờ đây sẽ không bị quá tải
                                        string apiKey = await apiKeyManager.AcquireKeyAsync(token);
                                        string translatedText = await TranslateTextAsync(result.OcrText, apiKey, options);
                                        finalResults[imagePath] = (result.OcrText, translatedText);
                                        translationSucceeded = true;
                                        Debug.WriteLine($"Dịch thành công cho '{Path.GetFileName(imagePath)}' ở lần thử {attempt}.");
                                        break; // Thoát vòng lặp retry khi thành công
                                    }
                                    catch (ApiException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests || apiEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                                    {
                                        Debug.WriteLine($"!!! Lỗi 429/503 khi DỊCH '{Path.GetFileName(imagePath)}'. Lần thử {attempt}/3.");
                                        if (attempt < 3) await Task.Delay(1000, token); // Chờ 1s trước khi thử lại
                                    }
                                }

                                if (!translationSucceeded)
                                {
                                    finalResults[imagePath] = (result.OcrText, "Lỗi: Dịch thất bại sau nhiều lần thử.");
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.WriteLine($"Tác vụ dịch cho '{Path.GetFileName(imagePath)}' đã bị hủy.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Lỗi nghiêm trọng khi dịch '{Path.GetFileName(imagePath)}': {ex.Message}");
                            if (finalResults.TryGetValue(imagePath, out var result))
                            {
                                finalResults[imagePath] = (result.OcrText, $"Lỗi dịch: {ex.Message}");
                            }
                        }
                        finally
                        {
                            // RẤT QUAN TRỌNG: Trả lại "vé" cho semaphore để tác vụ khác có thể chạy.
                            translationSemaphore.Release();
                            Debug.WriteLine($"Tác vụ dịch cho '{Path.GetFileName(imagePath)}' đã hoàn tất và trả lại vé.");
                        }
                    }).ToList();

                    await Task.WhenAll(translationTasks);
                    Debug.WriteLine("Đã hoàn tất tất cả các tác vụ dịch thuật.");
                }

                string resultFilePath = Path.Combine(selectedFolder, "_KếtQuả.txt");
                using (var writer = new StreamWriter(resultFilePath, false, Encoding.UTF8))
                {
                    foreach (var imagePath in imageFiles)
                    {
                        if (finalResults.TryGetValue(imagePath, out var result))
                        {
                            await writer.WriteLineAsync($"--- {Path.GetFileName(imagePath)} ---");
                            await writer.WriteLineAsync("OCR:\n" + result.OcrText + "\n");
                            if (!onlyOcr)
                            {
                                await writer.WriteLineAsync("Dịch:\n" + result.TranslatedText + "\n");
                            }
                        }
                    }
                }
                Debug.WriteLine($"Đã ghi tất cả kết quả vào tệp: {resultFilePath}");
                UpdateStatus("Hoàn tất!", false);
                ShowCompletionNotification("Xử lý thư mục hoàn tất", $"Kết quả đã được lưu vào tệp '{Path.GetFileName(resultFilePath)}'.");
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Tác vụ đã bị hủy.", true);
                Debug.WriteLine("Tác vụ xử lý thư mục đã bị hủy bởi người dùng.");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Lỗi tổng thể khi xử lý thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Đã xảy ra lỗi nghiêm trọng.", true);
                Debug.WriteLine($"!!! LỖI TỔNG THỂ trong ProcessFolder: {ex.ToString()}");
            }
            finally
            {
                SetButtonsEnabled(true);
                MainProgressBar.Visibility = Visibility.Collapsed;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                Debug.WriteLine("Kết thúc hàm ProcessFolder, đã dọn dẹp tài nguyên.");
            }
        }
        private string GetOcrPromptForBatch(string genre, int imageCount)
        {
            string baseInstruction = $"You are an expert OCR engine. You will be given {imageCount} separate comic page(s). Process each one individually based on the reading direction. Return the dialogue text for all pages, separated by the exact delimiter '------'. Do not add any other commentary.";
            string directionInstruction;

            if (genre == "Manga (Phải->Trái)")
            {
                directionInstruction = "CRITICAL INSTRUCTION: The reading direction is RIGHT-TO-LEFT, top-to-bottom. Scan panels in this order.";
            }
            else
            {
                directionInstruction = "CRITICAL INSTRUCTION: The reading direction is LEFT-TO-RIGHT, top-to-bottom. Scan panels in this order.";
            }

            return $"{baseInstruction}\n\n{directionInstruction}";
        }
        private async Task<string> SendMultiPartRequestAsync(List<object> parts, string apiKey, ProcessingOptions options)
        {
            string model = options.OcrModel ?? "gemini-1.5-flash-latest";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            // Thêm safety_settings vào request body
            var requestBody = new
            {
                contents = new[] { new { parts } },
                safety_settings = GetDisabledSafetySettings() // <--- THÊM MỚI
            };

            Debug.WriteLine($"[OCR Request] Gửi tới model {model} với {parts.Count - 1} ảnh.");
            return await PostApiRequest(url, requestBody);
        }

        private async Task<string> TranslateTextAsync(string textToTranslate, string apiKey, ProcessingOptions options)
        {
            if (string.IsNullOrWhiteSpace(textToTranslate) || textToTranslate.StartsWith("Lỗi:"))
            {
                return "Không có nội dung hợp lệ để dịch.";
            }

            string model = options.TranslateModel ?? "gemini-1.5-flash-latest";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            string promptText;
            object systemInstruction = null;

            if (options.TargetLanguage == "Tiếng Anh")
            {
                promptText = $"Translate the following text to English. Keep character names and original line breaks.\nText:\n{textToTranslate}";
            }
            else
            {
                switch (options.Genre)
                {
                    case "Tiên Hiệp / Kiếm Hiệp (Trái->Phải)":
                        promptText = $"Dịch nội dung sau sang tiếng Việt.\nNội dung:\n\"{textToTranslate}\"";
                        systemInstruction = new { parts = new[] { new { text = "Bạn là dịch giả chuyên truyện tiên hiệp. Dịch với văn phong cổ trang, huyền huyễn. Dùng từ Hán Việt cho tên riêng, địa danh, công pháp. Dùng xưng hô cổ trang (ví dụ: ngươi, ta, hắn, tại hạ, bần đạo). Chỉ trả về nội dung đã dịch, không giải thích, không thêm ghi chú." } } };
                        break;

                    case "Manga (Phải->Trái)":
                        promptText = $"Dịch sang Tiếng Việt, văn phong manga hiện đại. Chỉ trả về nội dung đã dịch, không thêm ghi chú, không giải thích.\nNội dung:\n{textToTranslate}";
                        break;

                    case "Tiêu chuẩn / Đô thị (Trái->Phải)":
                    case "Manhwa / Webtoon (Trái->Phải, Dài)":
                    default:
                        promptText = $"Dịch sang Tiếng Việt, phong cách đô thị hiện đại. Không dùng xưng hô 'tao', 'mày'. Giữ nguyên xuống dòng. Chỉ trả về nội dung đã dịch, không thêm ghi chú, không giải thích.\nNội dung:\n{textToTranslate}";
                        break;
                }
            }
            if (!string.IsNullOrWhiteSpace(options.CustomTranslatePrompt))
            {
                promptText += $"\n\nCHỈ DẪN BỔ SUNG QUAN TRỌNG:\n{options.CustomTranslatePrompt}";
            }

            var requestData = new
            {
                contents = new[] { new { parts = new[] { new { text = promptText } } } },
                system_instruction = systemInstruction,
                generation_config = new { response_mime_type = "text/plain" },
                safety_settings = GetDisabledSafetySettings() // <--- THÊM MỚI
            };

            Debug.WriteLine($"[Translate Request] Gửi tới model {model}.");
            return await PostApiRequest(url, requestData);
        }
        private object[] GetDisabledSafetySettings()
        {
            return new object[]
            {
        new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
        new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
        new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
        new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
            };
        }
        private async Task<string> PostApiRequest(string url, object requestBody)
        {
            const int MinIntervalMs = 1000; 

            long previousTicks = Interlocked.Read(ref _lastApiCallTicks);
            long nowTicks = DateTime.UtcNow.Ticks;

            var timeSinceLastCall = new TimeSpan(nowTicks - previousTicks);

            if (timeSinceLastCall.TotalMilliseconds < MinIntervalMs)
            {
                int delayTime = MinIntervalMs - (int)timeSinceLastCall.TotalMilliseconds;
                Debug.WriteLine($"--- GLOBAL RATE LIMITER: Request quá nhanh, đang chờ {delayTime}ms. ---");
                await Task.Delay(delayTime);
            }

            // Cập nhật lại thời gian của lần gọi cuối cùng sau khi đã delay (nếu có)
            Interlocked.Exchange(ref _lastApiCallTicks, DateTime.UtcNow.Ticks);
            // === KẾT THÚC PHẦN CODE MỚI ===


            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            string jsonContent = JsonSerializer.Serialize(requestBody, options);
            using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            Debug.WriteLine($"Gửi request tới: {url}");
            HttpResponseMessage response = await _httpClient.PostAsync(url, httpContent, cancellationToken);
            string responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException($"Lỗi API {response.StatusCode}: {responseString}", response.StatusCode);
            }
            return ParseGeminiResponse(responseString);
        }
        private string ParseGeminiResponse(string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse)) return "Lỗi: Phản hồi API rỗng.";
            try { using JsonDocument doc = JsonDocument.Parse(jsonResponse); JsonElement root = doc.RootElement; if (root.TryGetProperty("candidates", out JsonElement candidates) && candidates.GetArrayLength() > 0) { var firstCandidate = candidates[0]; if (firstCandidate.TryGetProperty("finishReason", out var reason) && reason.GetString() == "SAFETY") { return "Lỗi: Nội dung bị chặn bởi cài đặt an toàn của API."; } if (firstCandidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0) { return parts[0].TryGetProperty("text", out var text) ? text.GetString() : "Không có nội dung text từ API."; } } if (root.TryGetProperty("error", out var error)) { return $"Lỗi API: {error.GetProperty("message").GetString()}"; } return "Lỗi: Không thể phân tích phản hồi API."; }
            catch (JsonException jsonEx) { return $"Lỗi phân tích JSON: {jsonEx.Message}"; }
        }
        #endregion
        public class ProcessingOptions
        {
            public string OcrModel { get; set; }
            public string TranslateModel { get; set; }
            public string TargetLanguage { get; set; }
            public bool IsMangaMode { get; set; }
            public bool IsTienHiepMode { get; set; }
            public string Genre { get; set; }
            public string CustomTranslatePrompt { get; set; }
        }
        #region UI Helpers
        private void SetButtonsEnabled(bool isEnabled) { Dispatcher.Invoke(() => { SelectImageButton.IsEnabled = isEnabled; OCRAndTranslateButton.IsEnabled = isEnabled; SelectFolderOCRButton.IsEnabled = isEnabled; SelectFolderButton.IsEnabled = isEnabled; ManageApiKeysButton.IsEnabled = isEnabled; }); }
        private void UpdateStatus(string message, bool isError = false) { Dispatcher.Invoke(() => { StatusTextBlock.Text = message; StatusTextBlock.Foreground = isError ? (System.Windows.Media.Brush)FindResource("ErrorColor") : (System.Windows.Media.Brush)FindResource("TextSecondaryColor"); }); }
        private void ShowCompletionNotification(string title, string message) { Dispatcher.Invoke(() => { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Activate(); } CustomMessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information); }); }
        #endregion
    }
}