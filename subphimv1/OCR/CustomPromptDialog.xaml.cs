using System.Windows;

namespace subphimv1
{
    public partial class CustomPromptDialog : Window
    {
        public string PromptText { get; private set; }

        public CustomPromptDialog(string existingPrompt)
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow; // Đặt cửa sổ chính làm chủ sở hữu
            PromptTextBox.Text = existingPrompt;
            PromptTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            PromptText = PromptTextBox.Text;
            this.DialogResult = true;
            this.Close();
        }
    }
}