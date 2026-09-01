using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    public partial class ApiKeysDialog : Window
    {
        // Một thuộc tính public để cửa sổ chính có thể lấy danh sách keys sau khi dialog đóng
        public List<string> ApiKeys { get; private set; }

        public ApiKeysDialog(IEnumerable<string> currentKeys)
        {
            InitializeComponent();

            // Hiển thị danh sách keys hiện tại trong TextBox khi dialog được mở
            if (currentKeys != null && currentKeys.Any())
            {
                KeysTextBox.Text = string.Join(Environment.NewLine, currentKeys);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Lấy các key từ TextBox, làm sạch chúng (xóa khoảng trắng, dòng trống)
            ApiKeys = KeysTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(key => key.Trim())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToList();

            // Đặt DialogResult = true để báo cho cửa sổ chính biết rằng người dùng đã bấm lưu
            this.DialogResult = true;
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cả nút "Hủy" và nút "X" trên thanh tiêu đề đều gọi hàm này
            this.DialogResult = false;
        }
    }
}

    
