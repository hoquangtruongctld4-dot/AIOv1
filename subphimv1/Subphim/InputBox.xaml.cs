using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace subphimv1.Services
{
    public partial class InputBox : Window
    {
        public string ResponseText => ResponseTextBox.Text;

        public InputBox(string prompt, string title = "Input", string defaultValue = "")
        {
            InitializeComponent();
            this.Title = title;
            PromptText.Text = prompt;
            ResponseTextBox.Text = defaultValue;
            this.Loaded += (s, e) =>
            {
                ResponseTextBox.Focus();
                ResponseTextBox.SelectAll();
            };
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        public static string Show(string prompt, string title = "Input", string defaultValue = "")
        {
            InputBox dialog = new InputBox(prompt, title, defaultValue)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true)
            {
                return dialog.ResponseText;
            }
            return null;
        }
    }
}