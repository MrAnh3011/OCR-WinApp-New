using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

/// <summary>
/// Shared JSON-generating AI client for all AI-backed screens.
/// The active provider/model/url/key is selected from the build-time AI profile.
///
/// <para><paramref name="escalatedReasoningEffort"/> (tham số của <see cref="GenerateJsonAsync"/>) là
/// OPT-IN: khi truyền, mọi lượt thử LẠI (attempt ≥ 2) sẽ dựng lại request với mức reasoning cao hơn thay
/// vì lặp y nguyên mức của lượt đầu. Chỉ hai màn OCR GCN (iLIS/VietBD) dùng — 3 màn Tách GCN và Đất Uỷ
/// Ban giữ nguyên effort cố định để không đổi chi phí của chúng.</para>
/// </summary>
public interface IAiModelClient
{
    Task<string> GenerateJsonAsync(
        IReadOnlyList<object> content,
        int maxAttempts = 3,
        int? maxTokens = null,
        double? temperature = null,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? pdfParserEngine = null,
        bool enableRouterMetadata = false,
        int timeoutSeconds = 180,
        object? responseSchema = null,
        string? escalatedReasoningEffort = null,
        CancellationToken ct = default);
}
