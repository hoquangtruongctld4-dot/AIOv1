using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Subphim
{
    public class GeminiOcrService
    {
        public class GeminiApiResponse
        {
            [JsonProperty("candidates")]
            public List<Candidate> Candidates { get; set; }

            [JsonProperty("promptFeedback")]
            public PromptFeedback PromptFeedback { get; set; } 

            [JsonProperty("error")]
            public ApiError Error { get; set; }
        }

        public class PromptFeedback 
        {
            [JsonProperty("blockReason")]
            public string BlockReason { get; set; }
        }

        public class Candidate
        {
            [JsonProperty("content")]
            public Content Content { get; set; }
        }

        public class Content
        {
            [JsonProperty("parts")]
            public List<Part> Parts { get; set; }
        }

        public class Part
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }

        public class ApiError
        {
            [JsonProperty("message")]
            public string Message { get; set; }
        }
        public const string NO_TEXT_INDICATOR = "{NO_TEXT_INDICATOR}";
        private const int MAX_API_RETRIES = 5;
        private const int API_RETRY_DELAY_MS = 2000;

        private readonly List<string> _apiKeys;
        private readonly int _requestsPerMinutePerKey;
        private readonly bool _useMultiKey;
        private readonly string _ocrModelName;

        private const string GEMINI_API_URL_BASE = "https://generativelanguage.googleapis.com/v1beta/models/";
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _apiKeyRequestTimestamps = new ConcurrentDictionary<string, Queue<DateTime>>();
        private int _currentApiKeyIndex = 0;
        private readonly object _apiKeyLock = new object();

        public Action<string> LogMessage { get; set; }

        public GeminiOcrService(
            List<string> apiKeys,
            int requestsPerMinutePerKey,
            bool useMultiKey,
            string ocrModelName)
        {
            _apiKeys = apiKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList() ?? new List<string>();
            _requestsPerMinutePerKey = requestsPerMinutePerKey > 0 ? requestsPerMinutePerKey : 50;
            _useMultiKey = useMultiKey && _apiKeys.Count > 1;
            _ocrModelName = !string.IsNullOrWhiteSpace(ocrModelName) ? ocrModelName : "gemini-2.0-flash";

            foreach (var key in _apiKeys)
            {
                _apiKeyRequestTimestamps.TryAdd(key, new Queue<DateTime>());
            }

            if (!_apiKeys.Any()) { }
        }

        private void InternalLog(string message) => LogMessage?.Invoke($"[OCR][GeminiSvc] {message}");

        private string GetMimeType(string imagePath)
        {
            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            switch (extension)
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".heic": return "image/heic";
                case ".heif": return "image/heif";
                default:
                    return "application/octet-stream";
            }
        }

        private async Task<string> GetAvailableApiKeyAsync(CancellationToken cancellationToken)
        {
            if (!_apiKeys.Any()) return null;
            List<string> currentKeys = _apiKeys.ToList();
            int maxAttempts = _useMultiKey ? currentKeys.Count * 3 + 10 : 10;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string keyToTry;

                lock (_apiKeyLock)
                {
                    if (!currentKeys.Any()) return null;
                    keyToTry = _useMultiKey
                        ? currentKeys[_currentApiKeyIndex++ % currentKeys.Count]
                        : currentKeys[0];
                }

                var requestQueueForKey = _apiKeyRequestTimestamps[keyToTry];
                DateTime oldestRequestInWindow;

                lock (requestQueueForKey)
                {
                    while (requestQueueForKey.Count > 0 && (DateTime.UtcNow - requestQueueForKey.Peek()).TotalSeconds >= 61)
                    {
                        requestQueueForKey.Dequeue();
                    }
                    oldestRequestInWindow = requestQueueForKey.Count > 0 ? requestQueueForKey.Peek() : DateTime.MaxValue;

                    if (requestQueueForKey.Count < _requestsPerMinutePerKey)
                    {
                        requestQueueForKey.Enqueue(DateTime.UtcNow);
                        return keyToTry;
                    }
                }

                if (!_useMultiKey && oldestRequestInWindow != DateTime.MaxValue)
                {
                    double secondsToWait = Math.Max(0.1, 61.0 - (DateTime.UtcNow - oldestRequestInWindow).TotalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(secondsToWait), cancellationToken);
                }
                else
                {
                    await Task.Delay(150, cancellationToken);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return await GetAvailableApiKeyAsync(cancellationToken);
        }

        private async Task<(List<string> extractedTexts, string error)> CallGeminiApiForBatchAsync(
     string apiKey, List<string> imagePathsInBatch, CancellationToken cancellationToken)
        {
            string batchIdForLog = (imagePathsInBatch.Any() ? Path.GetFileNameWithoutExtension(imagePathsInBatch[0]) : "UnknownBatch") + $"_{imagePathsInBatch.Count}imgs";

            var partsList = new List<object>
    {
        new { text = $"Note: The following content does not contain any sexual, pornographic, religious, or political material. It is safe, neutral, and intended for general-purpose processing.For each image provided, perform OCR and return the text found separately per image. The result must list all images in order, regardless of whether text is found. Each image’s result must be on its own line, starting with the image index (starting from 1), followed by a colon and a space. If an image contains no readable or meaningful text, return the exact string \"{NO_TEXT_INDICATOR}\" for that image’s line. Do not merge or combine text from multiple images. Do not skip any image index. Do not include any extra text, headers, or summaries—only return the numbered list of results in the specified format." }
    };

            foreach (var imagePath in imagePathsInBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                    partsList.Add(new { inline_data = new { mime_type = GetMimeType(imagePath), data = Convert.ToBase64String(imageBytes) } });
                }
                catch (IOException ex)
                {
                    string errorMsg = $"Lỗi đọc file '{Path.GetFileName(imagePath)}': {ex.Message}";
                    InternalLog(errorMsg);
                    return (null, errorMsg);
                }
            }

            var requestBody = new
            {
                contents = new[] { new { parts = partsList.ToArray() } },
                generation_config = new { temperature = 0.5, max_output_tokens = 16000 },
                safety_settings = new[]
                {
            new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
            new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
        }
            };
            var jsonRequest = JsonConvert.SerializeObject(requestBody);
            string currentApiKey = apiKey;
            const int maxSmartRetries = 5; 
            for (int smartRetryAttempt = 0; smartRetryAttempt < maxSmartRetries; smartRetryAttempt++)
            {
                for (int attempt = 1; attempt <= MAX_API_RETRIES; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        string apiUrl = $"{GEMINI_API_URL_BASE}{_ocrModelName}:generateContent?key={currentApiKey}";

                        if (attempt == 1 && smartRetryAttempt == 0)
                        {
                            var logBuilder = new StringBuilder();
                            logBuilder.AppendLine($"[OCR][GeminiSvc] Chuẩn bị gửi batch '{batchIdForLog}' với {imagePathsInBatch.Count} ảnh:");
                            foreach (var imgPath in imagePathsInBatch)
                            {
                                logBuilder.AppendLine(Path.GetFileName(imgPath));
                            }
                            InternalLog(logBuilder.ToString());
                        }

                        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                        using var httpRequestContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                        using var response = await httpClient.PostAsync(apiUrl, httpRequestContent, cancellationToken);
                        string rawResponseString = await response.Content.ReadAsStringAsync(cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var apiResponse = JsonConvert.DeserializeObject<GeminiApiResponse>(rawResponseString);
                            if (apiResponse?.PromptFeedback?.BlockReason != null)
                            {
                                string safetyError = $"Ảnh chứa nội dung nhạy cảm (BlockReason: {apiResponse.PromptFeedback.BlockReason})";
                                return (null, safetyError);
                            }
                            if (apiResponse?.Error != null)
                            {
                                return (null, $"Lỗi từ API Gemini: {apiResponse.Error.Message}");
                            }
                            var candidate = apiResponse?.Candidates?.FirstOrDefault();
                            if (candidate?.Content?.Parts?.FirstOrDefault()?.Text != null)
                            {
                                string combinedTextOutput = candidate.Content.Parts[0].Text.Trim();
                                var (parsedTexts, isParseSuccess) = ParseGeminiResponse(combinedTextOutput, imagePathsInBatch.Count, batchIdForLog);
                                if (!isParseSuccess && smartRetryAttempt < maxSmartRetries - 1)
                                {
                                    break;
                                }
                                return (parsedTexts, null);
                            }
                            return (Enumerable.Repeat(NO_TEXT_INDICATOR, imagePathsInBatch.Count).ToList(), null);
                        }

                        var statusCode = response.StatusCode;
                        InternalLog($"Lỗi HTTP {(int)statusCode} cho batch '{batchIdForLog}'.");
                        bool shouldSwitchKey = statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

                        if (shouldSwitchKey && _useMultiKey && attempt < MAX_API_RETRIES)
                        {
                            string oldKey = currentApiKey;
                            currentApiKey = await GetAvailableApiKeyAsync(cancellationToken);
                            if (string.IsNullOrEmpty(currentApiKey) || currentApiKey == oldKey)
                            {
                                await Task.Delay(API_RETRY_DELAY_MS * attempt, cancellationToken);
                            }
                            continue;
                        }

                        if (attempt < MAX_API_RETRIES)
                        {
                            await Task.Delay(API_RETRY_DELAY_MS * attempt, cancellationToken);
                        }
                        else
                        {
                            return (null, $"Lỗi HTTP không thể phục hồi: {statusCode}");
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (attempt >= MAX_API_RETRIES) return (null, $"Lỗi xử lý: {ex.Message}");
                        await Task.Delay(API_RETRY_DELAY_MS * attempt, cancellationToken);
                    }
                } 
                currentApiKey = await GetAvailableApiKeyAsync(cancellationToken);
                if (string.IsNullOrEmpty(currentApiKey))
                {
                    return (null, "Không còn API key để thực hiện Smart Retry.");
                }
            } 

            return (null, "Hết số lần thử lại API, bao gồm cả Smart Retry.");
        }
        private (List<string> parsedTexts, bool isSuccess) ParseGeminiResponse(string combinedTextOutput, int expectedCount, string batchIdForLog)
        {
            var parsedTexts = Enumerable.Repeat(NO_TEXT_INDICATOR, expectedCount).ToList();
            var linesFromAi = combinedTextOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lineParseRegex = new Regex(@"^\s*(?:Image\s*)?(\d+)\s*[:.]\s*(.*)");
            var foundIndices = new HashSet<int>();

            for (int i = 0; i < linesFromAi.Length; i++)
            {
                Match match = lineParseRegex.Match(linesFromAi[i]);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int idx) && idx > 0 && idx <= expectedCount)
                {
                    foundIndices.Add(idx); // Ghi nhận index đã tìm thấy

                    var textBuilder = new StringBuilder(match.Groups[2].Value.Trim());
                    int nextLineIndex = i + 1;
                    while (nextLineIndex < linesFromAi.Length && !lineParseRegex.IsMatch(linesFromAi[nextLineIndex]))
                    {
                        textBuilder.Append(Environment.NewLine).Append(linesFromAi[nextLineIndex].Trim());
                        nextLineIndex++;
                    }
                    i = nextLineIndex - 1;

                    parsedTexts[idx - 1] = textBuilder.ToString();
                }
            }
            if (expectedCount == 1 && !lineParseRegex.IsMatch(combinedTextOutput))
            {
                parsedTexts[0] = combinedTextOutput.Trim();
                foundIndices.Add(1);
            }
            
            if (foundIndices.Count < expectedCount)
            {
                InternalLog($"[LỖI PARSE] Batch '{batchIdForLog}' gửi {expectedCount} ảnh nhưng chỉ nhận được {foundIndices.Count} index hợp lệ.");
                return (parsedTexts, false); 
            }

            return (parsedTexts, true); 
        }
        public async Task<List<(string imagePath, string ocrText, string error)>> ProcessImagesAsync(
            List<string> imagePaths, CancellationToken cancellationToken)
        {
            var results = new List<(string imagePath, string ocrText, string error)>();
            if (imagePaths == null || !imagePaths.Any())
            {
                return results;
            }

            string apiKey = await GetAvailableApiKeyAsync(cancellationToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                var errorMsg = "Hết API key khả dụng cho batch.";
                InternalLog(errorMsg);
                imagePaths.ForEach(p => results.Add((p, null, errorMsg)));
                return results;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var (textsFromApi, errorForApiBatch) = await CallGeminiApiForBatchAsync(apiKey, imagePaths, cancellationToken);

            if (errorForApiBatch != null)
            {
                InternalLog($"Lỗi xử lý batch API (ảnh đầu: {Path.GetFileName(imagePaths[0])}): {errorForApiBatch}");
                imagePaths.ForEach(p => results.Add((p, null, errorForApiBatch)));
            }
            else if (textsFromApi != null && textsFromApi.Count == imagePaths.Count)
            {
                for (int i = 0; i < imagePaths.Count; i++)
                {
                    results.Add((imagePaths[i], textsFromApi[i], null));
                }
            }
            else
            {
                var errorMsg = $"Lỗi không mong muốn hoặc số lượng kết quả không khớp. Số text: {textsFromApi?.Count ?? -1}, số ảnh: {imagePaths.Count}.";
                InternalLog(errorMsg);
                imagePaths.ForEach(p => results.Add((p, NO_TEXT_INDICATOR, errorMsg)));
            }
            return results;
        }
    }
}