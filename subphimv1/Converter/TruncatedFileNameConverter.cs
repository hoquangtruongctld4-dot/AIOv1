using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace subphimv1.Converters
{
    public class TruncatedFileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string fileName || string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            try
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                int maxLength = 25; // Tổng chiều dài tối đa mong muốn
                int partLength = 10;  // Chiều dài của phần đầu và phần cuối

                if (nameWithoutExt.Length > maxLength)
                {
                    return $"{nameWithoutExt.Substring(0, partLength)}...{nameWithoutExt.Substring(nameWithoutExt.Length - partLength)}{extension}";
                }
                else
                {
                    return fileName;
                }
            }
            catch
            {
                return value; // Trả về giá trị gốc nếu có lỗi
            }
        }

        // >>> PHẦN SỬA LỖI BẮT ĐẦU TỪ ĐÂY <<<
        // Chữ ký của phương thức đã được sửa lại cho đúng với interface IValueConverter
        // Sửa từ: public object ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        // Thành: public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Phương thức này không cần thiết cho việc hiển thị, nên chỉ cần throw exception là đủ
            throw new NotImplementedException();
        }
        // >>> KẾT THÚC PHẦN SỬA LỖI <<<
    }
}