using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace subphimv1
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; }

        // Constructor private giờ sẽ nhận owner
        private CustomMessageBox(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            InitializeComponent();

            // Gán Owner nếu được cung cấp, nếu không thì tự tìm cửa sổ active
            this.Owner = owner ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive);

            this.Title = title;
            TitleText.Text = title;
            MessageText.Text = message;

            SetupButtons(buttons);
            SetupIcon(icon);

            if (OkButton.Visibility == Visibility.Visible)
            {
                OkButton.Style = GetPrimaryButtonStyle();
            }
            if (YesButton.Visibility == Visibility.Visible)
            {
                YesButton.Style = GetPrimaryButtonStyle();
            }
        }

        // === PHẦN SỬA LỖI CHÍNH ===

        // 1. Tạo một phương thức Show mới có 5 tham số để khớp với lời gọi của bạn
        public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var dialog = new CustomMessageBox(owner, message, title, buttons, icon);
            dialog.ShowDialog();
            return dialog.Result;
        }

        // 2. Sửa phương thức Show 4 tham số để nó gọi phương thức 5 tham số với owner là null
        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            // Lời gọi này sẽ hoạt động cho các trường hợp không cần chỉ định owner
            return Show(null, message, title, buttons, icon);
        }

        // Cập nhật các overload tiện ích khác để gọi đúng
        public static MessageBoxResult Show(string message) => Show(null, message, "", MessageBoxButton.OK, MessageBoxImage.None);
        public static MessageBoxResult Show(string message, string title) => Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.None);
        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons) => Show(null, message, title, buttons, MessageBoxImage.None);


        private void SetupButtons(MessageBoxButton buttons)
        {
            OkButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OkButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.OKCancel:
                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNoCancel:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void SetupIcon(MessageBoxImage icon)
        {
            IconText.Visibility = Visibility.Visible;
            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconText.Text = "\uE946"; // Info icon
                    IconText.Foreground = (System.Windows.Media.Brush)FindResource("AccentColor");
                    break;
                case MessageBoxImage.Question:
                    IconText.Text = "\uE897"; // Question mark icon
                    IconText.Foreground = (System.Windows.Media.Brush)FindResource("AccentColor");
                    break;
                case MessageBoxImage.Warning:
                    IconText.Text = "\uE7BA"; // Warning icon
                    IconText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    break;
                case MessageBoxImage.Error:
                    IconText.Text = "\uEA39"; // Error icon
                    IconText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorColor");
                    break;
                default:
                    IconText.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private Style GetPrimaryButtonStyle()
        {
            var style = new Style(typeof(Button), (Style)FindResource(typeof(Button)));
            style.Setters.Add(new Setter(Button.BackgroundProperty, FindResource("AccentColor")));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, FindResource("AccentColor")));
            style.Triggers.Add(new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true,
                Setters = { new Setter(Button.BackgroundProperty, FindResource("AccentHoverColor")) }
            });
            style.Triggers.Add(new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true,
                Setters = { new Setter(Button.BackgroundProperty, FindResource("AccentPressedColor")) }
            });
            return style;
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton == OkButton) Result = MessageBoxResult.OK;
            else if (clickedButton == YesButton) Result = MessageBoxResult.Yes;
            else if (clickedButton == NoButton) Result = MessageBoxResult.No;
            else if (clickedButton == CancelButton) Result = MessageBoxResult.Cancel;
            else Result = MessageBoxResult.None;

            this.DialogResult = true;
        }
    }
}