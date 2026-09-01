using subphimv1.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Subphim
{
    public class GoogleOcrProcessor
    {
        private readonly GoogleDriveService _driveService;
        private readonly SemaphoreSlim _semaphore;

        public event Action<string, string> ErrorImageCopied;

        public GoogleOcrProcessor(GoogleDriveService driveService, SemaphoreSlim semaphore)
        {
            _driveService = driveService;
            _semaphore = semaphore;
        }

        public async Task<List<(string imagePath, string ocrText, string error)>> ProcessBatchAsync(
            List<string> imageFiles,
            string rawTextsDir,
            string googleFolderId,
            CancellationToken token)
        {
            var allResults = new ConcurrentBag<(string imagePath, string ocrText, string error)>();
            var ocrTasks = new List<Task>();

            foreach (var imagePath in imageFiles)
            {
                if (token.IsCancellationRequested) break;

                await _semaphore.WaitAsync(token);

                ocrTasks.Add(Task.Run(async () =>
                {
                    string extractedText = null;
                    string taskError = null;
                    try
                    {
                        string imgName = Path.GetFileName(imagePath);
                        var (fileId, uploadError) = await _driveService.UploadAndConvertImageAsync(imagePath, imgName, googleFolderId, token, "zh-Hans");
                        token.ThrowIfCancellationRequested();

                        if (!string.IsNullOrEmpty(fileId))
                        {
                            string rawTxtPath = Path.Combine(rawTextsDir, Path.ChangeExtension(imgName, ".gdrive.raw.txt"));
                            var (downloadSuccess, downloadError) = await _driveService.DownloadTextFileAsync(fileId, rawTxtPath, token);
                            if (downloadSuccess)
                            {
                                extractedText = await File.ReadAllTextAsync(rawTxtPath, Encoding.UTF8, token);
                            }
                            else
                            {
                                taskError = $"Lỗi tải về text: {downloadError}";
                            }
                            await _driveService.DeleteFileAsync(fileId, token.IsCancellationRequested ? CancellationToken.None : token);
                        }
                        else
                        {
                            taskError = $"Lỗi tải lên/convert: {uploadError}";
                            if (uploadError != null && uploadError.Contains("VĨNH_VIỄN"))
                            {
                                ErrorImageCopied?.Invoke(imagePath, $"UPLOAD_FAIL_{imgName}");
                            }
                        }
                    }
                    catch (OperationCanceledException) { taskError = "Tác vụ bị hủy."; }
                    catch (Exception ex)
                    {
                        taskError = $"Lỗi không xác định: {ex.Message}";
                        ErrorImageCopied?.Invoke(imagePath, $"EXCEPTION_{Path.GetFileName(imagePath)}");
                    }
                    finally
                    {
                        allResults.Add((imagePath, extractedText, taskError));
                        _semaphore.Release();
                    }
                }, token));
            }

            await Task.WhenAll(ocrTasks);
            return allResults.ToList();
        }
    }
}