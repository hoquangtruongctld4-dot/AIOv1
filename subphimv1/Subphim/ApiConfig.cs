using System.Collections.Generic;

namespace subphimv1.Models // Đảm bảo namespace này là chính xác
{

    public static class ApiConfig
    {
        public const string DefaultChutesModelSrt = "deepseek/deepseek-r1-0528:free";
        public const string DefaultGeminiModelSrt = "gemini-2.5-flash";
        public const string DefaultGeminiModelOcr = "gemini-2.5-flash";
        public static readonly List<string> DefaultChatGPTModelsSrt = new List<string>
    {
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4-turbo", // Thay cho gpt-4.1 và 4.5 vì tên model thường là vậy
        "gpt-3.5-turbo"  // Thêm một model phổ biến
    };
    }

}