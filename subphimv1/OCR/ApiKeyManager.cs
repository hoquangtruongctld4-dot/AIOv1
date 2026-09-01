using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace subphimv1.Services
{
    /// <summary>
    /// Quản lý API keys với logic Rate Limiting dựa trên số lượng request mỗi phút.
    /// Mỗi key được quản lý ngân sách riêng.
    /// </summary>
    public class ApiKeyManagerV2
    {
        private readonly List<string> _keys;
        // Dùng ConcurrentDictionary để đảm bảo an toàn luồng khi truy cập
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestTimestamps = new ConcurrentDictionary<string, Queue<DateTime>>();
        private readonly object _lock = new object();

        // Cấu hình: 8 requests trong vòng 60 giây
        private const int RateLimitCount = 5;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

        public ApiKeyManagerV2(IEnumerable<string> apiKeys)
        {
            _keys = apiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            if (!_keys.Any())
            {
                throw new InvalidOperationException("Danh sách API key không được rỗng.");
            }

            foreach (var key in _keys)
            {
                // Mỗi key có một hàng đợi riêng để lưu trữ timestamp của các request gần đây
                _requestTimestamps[key] = new Queue<DateTime>();
            }
        }

        /// <summary>
        /// Lấy một API key khả dụng và "đặt chỗ" một slot request.
        /// Nếu tất cả các key đều đang bị giới hạn, phương thức sẽ đợi cho đến khi có key khả dụng.
        /// </summary>
        /// <param name="cancellationToken">Token để hủy việc chờ đợi.</param>
        /// <returns>Một API key có thể sử dụng ngay lập tức.</returns>
        public async Task<string> AcquireKeyAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string availableKey = FindAndAcquireAvailableKey();

                if (availableKey != null)
                {
                    // Tìm thấy key, trả về ngay lập tức
                    return availableKey;
                }

                // Nếu không có key nào khả dụng, tính toán thời gian chờ ngắn nhất
                // để một slot của key nào đó được giải phóng.
                var delay = CalculateMinimumWaitTime();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            throw new TaskCanceledException();
        }

        private string FindAndAcquireAvailableKey()
        {
            // Lock để đảm bảo việc tìm và cập nhật key diễn ra nhất quán
            lock (_lock)
            {
                // Ưu tiên các key ít được sử dụng nhất để phân bổ tải đều
                foreach (var key in _keys.OrderBy(k => _requestTimestamps[k].Count))
                {
                    var timestamps = _requestTimestamps[key];

                    // Dọn dẹp các timestamp đã cũ (ra khỏi cửa sổ 60 giây)
                    while (timestamps.Any() && timestamps.Peek() < DateTime.UtcNow - RateLimitWindow)
                    {
                        timestamps.Dequeue();
                    }

                    // Nếu số request trong cửa sổ vẫn còn dưới giới hạn
                    if (timestamps.Count < RateLimitCount)
                    {
                        // "Đặt chỗ" slot bằng cách thêm timestamp hiện tại
                        timestamps.Enqueue(DateTime.UtcNow);
                        return key; // Trả về key hợp lệ
                    }
                }
            }
            // Không tìm thấy key nào khả dụng
            return null;
        }

        private TimeSpan CalculateMinimumWaitTime()
        {
            var minWaitTime = TimeSpan.MaxValue;

            lock (_lock)
            {
                foreach (var key in _keys)
                {
                    var timestamps = _requestTimestamps[key];
                    if (timestamps.Any())
                    {
                        // Thời điểm mà request cũ nhất sẽ hết hạn (ra khỏi cửa sổ 60s)
                        var oldestRequestExpiryTime = timestamps.Peek() + RateLimitWindow;
                        var timeToWait = oldestRequestExpiryTime - DateTime.UtcNow;

                        if (timeToWait > TimeSpan.Zero && timeToWait < minWaitTime)
                        {
                            minWaitTime = timeToWait;
                        }
                    }
                }
            }

            // Thêm 100ms đệm để tránh các vấn đề về timing
            return (minWaitTime == TimeSpan.MaxValue) ? TimeSpan.FromMilliseconds(100) : minWaitTime + TimeSpan.FromMilliseconds(100);
        }
    }
}