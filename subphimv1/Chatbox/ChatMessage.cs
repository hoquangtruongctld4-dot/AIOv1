// Vị trí: Models/ChatMessage.cs
namespace subphimv1.Models
{
    public class ChatMessage
    {
        public string Username { get; set; }
        public string Message { get; set; }
        public bool IsMine { get; set; } // Để xác định tin nhắn có phải của mình không
        public DateTime Timestamp { get; set; }
    }
}