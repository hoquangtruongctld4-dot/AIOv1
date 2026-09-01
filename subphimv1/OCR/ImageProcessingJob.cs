using System.Collections.Generic;

namespace subphimv1.Services
{
    /// <summary>
    /// Định nghĩa loại công việc cần xử lý.
    /// </summary>
    public enum JobType
    {
        /// <summary>
        /// Một request chứa nhiều mảnh cắt của CÙNG MỘT ảnh.
        /// </summary>
        CutImageParts,
        /// <summary>
        /// Một request chứa nhiều ảnh gốc, KHÔNG CẮT.
        /// </summary>
        FullImageBatch
    }

    /// <summary>
    /// Đại diện cho MỘT yêu cầu API duy nhất, có thể chứa nhiều ảnh/mảnh ảnh.
    /// </summary>
    public class ImageProcessingJob
    {
        public JobType Type { get; set; }

        /// <summary>
        /// Danh sách các đường dẫn đến file GỐC.
        /// Sẽ có 1 đường dẫn cho loại CutImageParts, và nhiều đường dẫn cho FullImageBatch.
        /// </summary>
        public List<string> OriginalImagePaths { get; set; } = new List<string>();

        /// <summary>
        /// Danh sách dữ liệu của các ảnh/mảnh ảnh sẽ được gửi trong request này.
        /// </summary>
        public List<byte[]> ImageDatas { get; set; } = new List<byte[]>();
    }
}