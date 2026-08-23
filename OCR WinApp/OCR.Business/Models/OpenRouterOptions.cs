namespace OCR.Business.Models;

/// <summary>
/// Cấu hình OpenRouter DÙNG CHUNG (khai báo ở MỘT nơi duy nhất — mục "OpenRouter" trong appsettings.json)
/// cho mọi pipeline gọi OpenRouter: Đất Uỷ Ban, Tách GCN, GCN (New) khi Provider = openrouter|qwen.
/// </summary>
public sealed class OpenRouterOptions
{
    /// <summary>Khoá API OpenRouter. Để rỗng trong appsettings → lấy từ SecretStore (mã hoá).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>BaseUrl OpenRouter (client tự thêm "/chat/completions" nếu thiếu).</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
}
