using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

/// <summary>
/// Client gọi OpenRouter <c>chat/completions</c> DÙNG CHUNG cho mọi màn (Đất Uỷ Ban, Tách GCN, GCN New).
/// BaseUrl + ApiKey lấy từ MỘT nơi (OpenRouterOptions). Caller chỉ dựng phần <paramref name="content"/>
/// (text + ảnh/PDF) của message "user"; hàm trả về CHUỖI <c>choices[0].message.content</c> để caller tự
/// parse theo nhu cầu riêng (mỗi màn xử lý kết quả khác nhau).
/// </summary>
public interface IOpenRouterClient
{
    Task<string> ChatJsonAsync(
        string model,
        IReadOnlyList<object> content,
        int maxAttempts = 3,
        int? maxTokens = null,
        double? temperature = null,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? pdfParserEngine = null,
        bool enableRouterMetadata = false,
        int timeoutSeconds = 180,
        CancellationToken ct = default);
}
