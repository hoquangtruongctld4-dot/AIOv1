using System.Threading.Tasks;

namespace subphimv1.Services
{
    public class SmartCutService
    {
        private TaskCompletionSource<SmartCutMode?> _tcs;

        /// <summary>
        /// Bắt đầu một phiên yêu cầu lựa chọn từ người dùng và trả về Task để chờ kết quả.
        /// </summary>
        /// <returns>Một Task chứa chế độ SmartCut được chọn, hoặc null nếu bị hủy.</returns>
        public Task<SmartCutMode?> GetSmartCutModeAsync()
        {
            // Hủy phiên trước đó nếu có
            _tcs?.TrySetCanceled();

            _tcs = new TaskCompletionSource<SmartCutMode?>();
            return _tcs.Task;
        }

        /// <summary>
        /// Được gọi bởi UI khi người dùng chọn một chế độ.
        /// </summary>
        /// <param name="mode">Chế độ được chọn.</param>
        public void SetResult(SmartCutMode mode)
        {
            _tcs?.TrySetResult(mode);
        }

        /// <summary>
        /// Được gọi bởi UI khi người dùng hủy bỏ lựa chọn (ví dụ: đóng popup).
        /// </summary>
        public void Cancel()
        {
            _tcs?.TrySetResult(null);
        }
    }
}