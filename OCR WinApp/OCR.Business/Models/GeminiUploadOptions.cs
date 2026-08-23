namespace OCR.Business.Models;

/// <summary>
/// Cấu hình luồng upload Gemini Files API dùng chung cho mọi màn AI.
/// Nằm ở mục "GeminiUpload" trong appsettings.business.json.
/// </summary>
public sealed class GeminiUploadOptions
{
    /// <summary>Số worker upload chạy song song. Độc lập với số worker inference (5).</summary>
    public int Workers { get; set; } = 3;

    /// <summary>Số lượt gọi upload tối đa cho một artifact (tính cả lượt đầu).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Nghỉ giữa các lượt: lượt 1→2 nghỉ RetryBaseDelayMs, lượt 2→3 nghỉ gấp đôi. Test đặt 0.</summary>
    public int RetryBaseDelayMs { get; set; } = 2000;
}
