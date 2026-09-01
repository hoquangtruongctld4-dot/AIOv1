// Vị trí: ChatWindow.xaml.cs
using subphimv1.Models;
using subphimv1.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace subphimv1
{
    public partial class ChatWindow : Window
    {
        private readonly ChatService _chatService;
        private readonly string _currentUsername;
        public ObservableCollection<ChatMessage> Messages { get; set; }

        public ChatWindow(ChatService chatService, string currentUsername)
        {
            InitializeComponent();
            _chatService = chatService;
            _currentUsername = string.IsNullOrWhiteSpace(currentUsername) ? "Guest" : currentUsername;
            Messages = new ObservableCollection<ChatMessage>();
            MessagesItemsControl.ItemsSource = Messages;

            this.Loaded += ChatWindow_Loaded;
            this.Closing += ChatWindow_Closing;
        }

        private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {

            var history = await ApiService.GetFeedbackHistoryAsync();

            foreach (var item in history)
            {
                Messages.Add(new ChatMessage
                {
                });
            }
            ScrollToBottom();

            // 2. Lắng nghe tin nhắn mới
            _chatService.MessageReceived += OnMessageReceived;
            await _chatService.StartAsync(_currentUsername);
        }

        private async void ChatWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _chatService.MessageReceived -= OnMessageReceived;
            await _chatService.StopAsync();
        }

        private void OnMessageReceived(ChatMessage message)
        {


            Dispatcher.Invoke(() =>
            {
                Messages.Add(message);
                ScrollToBottom();
            });
        }

        private async void SendMessage()
        {
            string messageText = MessageTextBox.Text;
            if (string.IsNullOrWhiteSpace(messageText)) return;


            MessageTextBox.Clear();
            // Gọi API để gửi
            await _chatService.SendMessageAsync(_currentUsername, messageText);
        }

        private void ScrollToBottom()
        {
            MessageScrollViewer.ScrollToBottom();
        }

        // --- UI Event Handlers ---
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { this.Hide(); } // Chỉ ẩn đi thay vì đóng
        private void SendButton_Click(object sender, RoutedEventArgs e) => SendMessage();
        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SendMessage(); }
    }
}