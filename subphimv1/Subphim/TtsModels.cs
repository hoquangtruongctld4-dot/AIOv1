namespace subphimv1.Models
{
    // Lớp này được chỉnh sửa để chỉ chứa tên hiển thị và ID gửi lên API
    public class TtsVoice
    {
        public string DisplayName { get; set; }
        public string ApiId { get; set; }

        public TtsVoice(string displayName, string apiId)
        {
            DisplayName = displayName;
            ApiId = apiId;
        }
    }

    // Lớp TtsModel không còn cần thiết và có thể bị xóa hoặc bỏ qua
    public class TtsModel
    {
        public string DisplayName { get; set; }
        public string ApiId { get; set; }

        public TtsModel(string displayName, string apiId)
        {
            DisplayName = displayName;
            ApiId = apiId;
        }
    }

    // Lớp mới để quản lý ngôn ngữ TTS
    public class TtsLanguage
    {
        public string DisplayName { get; set; }
        public string LanguageCode { get; set; }

        public TtsLanguage(string displayName, string languageCode)
        {
            DisplayName = displayName;
            LanguageCode = languageCode;
        }
    }
}