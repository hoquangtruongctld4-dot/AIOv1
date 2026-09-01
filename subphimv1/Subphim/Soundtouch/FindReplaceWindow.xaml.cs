using subphimv1.Subphim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace subphimv1
{
    /// <summary>
    /// FindReplaceWindow - Cửa sổ Find and Replace cho subtitles
    /// Cho phép tìm kiếm và thay thế text trong cả OriginalText và TranslatedText
    /// </summary>
    public partial class FindReplaceWindow : Window, INotifyPropertyChanged
    {
        // ViewModel cho DataGrid
        private ObservableCollection<SubtitleItemViewModel> _subtitleItems;
        public ObservableCollection<SubtitleItemViewModel> SubtitleItems
        {
            get => _subtitleItems;
            set
            {
                _subtitleItems = value;
                OnPropertyChanged();
            }
        }

        // Danh sách subtitle gốc từ MainWindow
        private List<SrtSubtitleLine> _originalSubtitles;

        // Index hiện tại khi tìm kiếm
        private int _currentFindIndex = -1;

        // Danh sách các item match với search query
        private List<SubtitleItemViewModel> _matchedItems = new List<SubtitleItemViewModel>();

        // Action callback để cập nhật lại MainWindow khi user click OK
        private Action<List<SrtSubtitleLine>> _onApplyChanges;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="subtitles">Danh sách subtitle từ MainWindow</param>
        /// <param name="onApplyChanges">Callback để áp dụng thay đổi về MainWindow</param>
        public FindReplaceWindow(List<SrtSubtitleLine> subtitles, Action<List<SrtSubtitleLine>> onApplyChanges)
        {
            InitializeComponent();
            DataContext = this;

            _originalSubtitles = subtitles;
            _onApplyChanges = onApplyChanges;

            // Tạo ViewModel items từ subtitles
            SubtitleItems = new ObservableCollection<SubtitleItemViewModel>();
            foreach (var subtitle in subtitles)
            {
                SubtitleItems.Add(new SubtitleItemViewModel(subtitle));
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Focus vào TextBox tìm kiếm khi window load
            FindTextBox.Focus();
        }

        #region Find Logic

        /// <summary>
        /// Xử lý khi text trong FindTextBox thay đổi
        /// </summary>
        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Reset current find index khi search text thay đổi
            _currentFindIndex = -1;
            _matchedItems.Clear();

            if (string.IsNullOrEmpty(FindTextBox.Text))
            {
                StatusTextBlock.Text = "";
                return;
            }

            // Tìm tất cả các items match
            UpdateMatchedItems();

            if (_matchedItems.Count > 0)
            {
                StatusTextBlock.Text = $"Tìm thấy {_matchedItems.Count} kết quả";
            }
            else
            {
                StatusTextBlock.Text = "Không tìm thấy kết quả";
            }
        }

        /// <summary>
        /// Cập nhật danh sách các items match với search query
        /// </summary>
        private void UpdateMatchedItems()
        {
            _matchedItems.Clear();

            if (string.IsNullOrEmpty(FindTextBox.Text))
                return;

            string searchText = FindTextBox.Text;
            bool caseSensitive = CaseSensitiveCheckBox.IsChecked == true;
            bool searchOriginal = SearchOriginalCheckBox.IsChecked == true;
            bool searchTranslated = SearchTranslatedCheckBox.IsChecked == true;

            foreach (var item in SubtitleItems)
            {
                bool found = false;

                if (searchOriginal && !string.IsNullOrEmpty(item.OriginalText))
                {
                    if (ContainsText(item.OriginalText, searchText, caseSensitive))
                    {
                        found = true;
                    }
                }

                if (searchTranslated && !string.IsNullOrEmpty(item.TranslatedText))
                {
                    if (ContainsText(item.TranslatedText, searchText, caseSensitive))
                    {
                        found = true;
                    }
                }

                if (found)
                {
                    _matchedItems.Add(item);
                }
            }
        }

        /// <summary>
        /// Kiểm tra xem text có chứa search text không
        /// </summary>
        private bool ContainsText(string text, string searchText, bool caseSensitive)
        {
            if (caseSensitive)
            {
                return text.Contains(searchText);
            }
            else
            {
                return text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>
        /// Tìm kết quả tiếp theo
        /// </summary>
        private void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FindTextBox.Text))
            {
                MessageBox.Show("Vui lòng nhập từ khoá tìm kiếm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateMatchedItems();

            if (_matchedItems.Count == 0)
            {
                MessageBox.Show("Không tìm thấy kết quả!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Tìm next item
            _currentFindIndex++;
            if (_currentFindIndex >= _matchedItems.Count)
            {
                _currentFindIndex = 0;
            }

            SelectAndScrollToItem(_matchedItems[_currentFindIndex]);
            StatusTextBlock.Text = $"Kết quả {_currentFindIndex + 1}/{_matchedItems.Count}";
        }

        /// <summary>
        /// Tìm kết quả trước đó
        /// </summary>
        private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FindTextBox.Text))
            {
                MessageBox.Show("Vui lòng nhập từ khoá tìm kiếm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateMatchedItems();

            if (_matchedItems.Count == 0)
            {
                MessageBox.Show("Không tìm thấy kết quả!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Tìm previous item
            _currentFindIndex--;
            if (_currentFindIndex < 0)
            {
                _currentFindIndex = _matchedItems.Count - 1;
            }

            SelectAndScrollToItem(_matchedItems[_currentFindIndex]);
            StatusTextBlock.Text = $"Kết quả {_currentFindIndex + 1}/{_matchedItems.Count}";
        }

        /// <summary>
        /// Select và scroll tới item trong DataGrid
        /// </summary>
        private void SelectAndScrollToItem(SubtitleItemViewModel item)
        {
            SubtitlesDataGrid.SelectedItem = item;
            SubtitlesDataGrid.ScrollIntoView(item);
        }

        #endregion

        #region Replace Logic

        /// <summary>
        /// Thay thế text tại vị trí hiện tại
        /// </summary>
        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FindTextBox.Text))
            {
                MessageBox.Show("Vui lòng nhập từ khoá tìm kiếm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItem = SubtitlesDataGrid.SelectedItem as SubtitleItemViewModel;
            if (selectedItem == null)
            {
                MessageBox.Show("Vui lòng chọn một dòng phụ đề để thay thế!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string searchText = FindTextBox.Text;
            string replaceText = ReplaceTextBox.Text ?? "";
            bool caseSensitive = CaseSensitiveCheckBox.IsChecked == true;
            bool searchOriginal = SearchOriginalCheckBox.IsChecked == true;
            bool searchTranslated = SearchTranslatedCheckBox.IsChecked == true;

            bool replaced = false;

            // Thay thế trong OriginalText
            if (searchOriginal && !string.IsNullOrEmpty(selectedItem.OriginalText))
            {
                string newText = ReplaceText(selectedItem.OriginalText, searchText, replaceText, caseSensitive);
                if (newText != selectedItem.OriginalText)
                {
                    selectedItem.OriginalText = newText;
                    replaced = true;
                }
            }

            // Thay thế trong TranslatedText
            if (searchTranslated && !string.IsNullOrEmpty(selectedItem.TranslatedText))
            {
                string newText = ReplaceText(selectedItem.TranslatedText, searchText, replaceText, caseSensitive);
                if (newText != selectedItem.TranslatedText)
                {
                    selectedItem.TranslatedText = newText;
                    replaced = true;
                }
            }

            if (replaced)
            {
                StatusTextBlock.Text = "Đã thay thế 1 kết quả";
                // Tự động tìm tiếp
                FindNextButton_Click(null, null);
            }
            else
            {
                StatusTextBlock.Text = "Không có gì để thay thế";
            }
        }

        /// <summary>
        /// Thay thế tất cả các kết quả tìm được
        /// </summary>
        private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FindTextBox.Text))
            {
                MessageBox.Show("Vui lòng nhập từ khoá tìm kiếm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string searchText = FindTextBox.Text;
            string replaceText = ReplaceTextBox.Text ?? "";
            bool caseSensitive = CaseSensitiveCheckBox.IsChecked == true;
            bool searchOriginal = SearchOriginalCheckBox.IsChecked == true;
            bool searchTranslated = SearchTranslatedCheckBox.IsChecked == true;

            int replacedCount = 0;

            foreach (var item in SubtitleItems)
            {
                // Thay thế trong OriginalText
                if (searchOriginal && !string.IsNullOrEmpty(item.OriginalText))
                {
                    string newText = ReplaceText(item.OriginalText, searchText, replaceText, caseSensitive);
                    if (newText != item.OriginalText)
                    {
                        item.OriginalText = newText;
                        replacedCount++;
                    }
                }

                // Thay thế trong TranslatedText
                if (searchTranslated && !string.IsNullOrEmpty(item.TranslatedText))
                {
                    string newText = ReplaceText(item.TranslatedText, searchText, replaceText, caseSensitive);
                    if (newText != item.TranslatedText)
                    {
                        item.TranslatedText = newText;
                        replacedCount++;
                    }
                }
            }

            if (replacedCount > 0)
            {
                StatusTextBlock.Text = $"Đã thay thế {replacedCount} kết quả";
                MessageBox.Show($"Đã thay thế thành công {replacedCount} kết quả!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusTextBlock.Text = "Không có gì để thay thế";
                MessageBox.Show("Không tìm thấy kết quả nào để thay thế!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Thay thế text (chỉ thay thế lần đầu tiên tìm thấy)
        /// </summary>
        private string ReplaceText(string text, string searchText, string replaceText, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
                return text;

            if (caseSensitive)
            {
                // Thay thế phân biệt hoa thường
                int index = text.IndexOf(searchText);
                if (index >= 0)
                {
                    return text.Substring(0, index) + replaceText + text.Substring(index + searchText.Length);
                }
            }
            else
            {
                // Thay thế không phân biệt hoa thường
                int index = text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return text.Substring(0, index) + replaceText + text.Substring(index + searchText.Length);
                }
            }

            return text;
        }

        #endregion

        #region Button Events

        /// <summary>
        /// Xử lý khi user click OK - Áp dụng tất cả thay đổi về MainWindow
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Cập nhật tất cả thay đổi về original subtitles
            for (int i = 0; i < _originalSubtitles.Count && i < SubtitleItems.Count; i++)
            {
                var original = _originalSubtitles[i];
                var viewModel = SubtitleItems[i];

                // Cập nhật OriginalText
                if (original.OriginalText != viewModel.OriginalText)
                {
                    original.OriginalText = viewModel.OriginalText;
                }

                // Cập nhật TranslatedText
                if (original.TranslatedText != viewModel.TranslatedText)
                {
                    original.TranslatedText = viewModel.TranslatedText;
                }
            }

            // Gọi callback để MainWindow cập nhật UI và lưu project
            _onApplyChanges?.Invoke(_originalSubtitles);

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Xử lý khi user click Cancel - Huỷ tất cả thay đổi
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        #region DataGrid Events
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
        private void SubtitlesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Có thể thêm logic xử lý khi user select row
        }

        /// <summary>
        /// Xử lý khi user kết thúc edit cell
        /// </summary>
        private void SubtitlesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Đảm bảo binding được cập nhật ngay
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var item = e.Row.Item as SubtitleItemViewModel;
                if (item != null)
                {
                    // Binding sẽ tự động cập nhật nhờ INotifyPropertyChanged
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// ViewModel cho mỗi subtitle item trong DataGrid
    /// </summary>
    public class SubtitleItemViewModel : INotifyPropertyChanged
    {
        private SrtSubtitleLine _subtitle;

        public int Index => _subtitle.Index;
        public string TimeCode => _subtitle.TimeCode;

        private string _originalText;
        public string OriginalText
        {
            get => _originalText;
            set
            {
                if (_originalText != value)
                {
                    _originalText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _translatedText;
        public string TranslatedText
        {
            get => _translatedText;
            set
            {
                if (_translatedText != value)
                {
                    _translatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public SubtitleItemViewModel(SrtSubtitleLine subtitle)
        {
            _subtitle = subtitle;
            _originalText = subtitle.OriginalText;
            _translatedText = subtitle.TranslatedText;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
