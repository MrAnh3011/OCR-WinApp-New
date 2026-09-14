using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;

namespace OCR.Business.VbdBn;

/// <summary>
/// Luồng trích CCCD/CMND từ file GTK của màn OCR GCN VBD-BN: PDF → AI provider chung →
/// <see cref="CccdEnvelope"/>. Cache JSON theo cachePath do ViewModel cấp (cùng workspace với lô GCN
/// nhưng khác thư mục con). Prompt: PROMPT_TRICH_XUAT_CCCD.md (embedded resource).
/// </summary>
public sealed class CccdExtractService : ICccdExtractService
{
    private const int MaxOutputTokens = 65536;
    private const string DefaultReasoningEffort = "medium";
    private const string EscalatedReasoningEffort = "high";
    private const string PromptFileName = "PROMPT_TRICH_XUAT_CCCD.md";

    private readonly IAiModelClient _ai;
    private static string? _prompt;

    public CccdExtractService(IAiModelClient ai)
    {
        _ai = ai;
    }

    public async Task<CccdEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        void Log(string m) => logCallback?.Invoke(m);
        string fileName = Path.GetFileName(filePath);
        if (cachePath is null) throw new ArgumentNullException(nameof(cachePath),
            "Màn VBD-BN luôn cấp cachePath từ workspace — không có đường dẫn mặc định.");
        string cacheDir = Path.GetDirectoryName(cachePath)!;

        // 1. Cache: JSON mới hơn file nguồn thì dùng lại.
        if (useCachedJson && File.Exists(cachePath) &&
            File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(filePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<CccdEnvelope>(
                    await File.ReadAllTextAsync(cachePath, ct),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cached != null)
                {
                    cached.danh_sach_giay_to ??= new List<CccdRecord>();
                    Log($"⚡ Dùng cache JSON: {Path.GetFileName(cachePath)}");
                    return cached;
                }
            }
            catch { }
        }

        // 2. Gọi AI — GTK chỉ nhận .pdf (bộ lọc đầu vào đã chặn), gửi PDF trực tiếp.
        // Bọc prompt vào dict "type=text": AiModelClient.ToGeminiParts chỉ đọc content item dạng
        // dictionary (xem VietBdGcnExtractService.CallPdfDirectAsync) — thêm chuỗi thô sẽ bị bỏ qua.
        var prompt = LoadPrompt();
        var content = new List<object>
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = prompt }
        };
        if (uploadedFiles is { Count: > 0 })
        {
            foreach (var f in uploadedFiles) content.Add(ToGeminiFileContent(f));
        }
        else
        {
            // COPY NGUYÊN VĂN cách VietBdGcnExtractService.CallPdfDirectAsync dựng phần tử PDF
            // inline (đọc byte + bọc đúng kiểu object mà AiModelClient hiểu).
            content.Add(await BuildInlinePdfPartAsync(filePath, ct));
        }

        Log("📄 Gửi PDF GTK đến AI provider, tìm CMND/CCCD...");
        string responseStr = await _ai.GenerateJsonAsync(
            content,
            maxAttempts: 3,
            maxTokens: MaxOutputTokens,
            temperature: 0,
            reasoningEnabled: true,
            reasoningEffort: DefaultReasoningEffort,
            timeoutSeconds: 300,
            responseSchema: CccdResponseSchema.Instance,
            escalatedReasoningEffort: EscalatedReasoningEffort,
            ct: ct);

        // 3. Parse.
        var envelope = ParseEnvelope(responseStr);
        envelope.ten_file = fileName;

        // 4. Ghi cache — lỗi ghi cache KHÔNG được làm hỏng kết quả extract đã có (copy nguyên văn
        // VietBdGcnExtractService.SaveCacheAsync).
        try
        {
            Directory.CreateDirectory(cacheDir);
            var cacheOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(envelope, cacheOptions), ct);
        }
        catch { }
        Log($"🪪 Đọc được {envelope.danh_sach_giay_to.Count} giấy tờ tuỳ thân.");
        return envelope;
    }

    private static CccdEnvelope ParseEnvelope(string responseStr)
    {
        string cleanJson = CleanJsonBlock(responseStr);
        try
        {
            var envelope = JsonSerializer.Deserialize<CccdEnvelope>(
                cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope is null) throw new Exception("Kết quả rỗng.");
            envelope.danh_sach_giay_to ??= new List<CccdRecord>();
            return envelope;
        }
        catch (Exception ex)
        {
            throw new Exception($"Mô hình không trả về JSON đúng cấu trúc CCCD: {ex.Message}");
        }
    }

    // ---- Copy nguyên văn từ VietBdGcnExtractService.cs ----

    /// <summary>
    /// Copy nguyên văn từ <c>VietBdGcnExtractService.ToGeminiFileContent</c>: bọc
    /// <see cref="GeminiFileReference"/> (đã upload sẵn) thành dict "type=file_uri" mà
    /// <c>AiModelClient.ToGeminiParts</c> hiểu — GeminiFileReference tự thân KHÔNG phải dictionary
    /// nên không thể thêm thẳng vào content.
    /// </summary>
    private static Dictionary<string, object> ToGeminiFileContent(GeminiFileReference file)
        => new()
        {
            ["type"] = "file_uri",
            ["file_uri"] = new Dictionary<string, string>
            {
                ["mime_type"] = file.MimeType,
                ["file_uri"] = file.Uri
            }
        };

    /// <summary>
    /// Copy nguyên văn cách <c>VietBdGcnExtractService.CallPdfDirectAsync</c> dựng phần tử PDF inline
    /// (nhánh không dùng Gemini File API): đọc byte PDF và bọc thành dict "type=file" mà
    /// <c>AiModelClient</c> hiểu.
    /// </summary>
    private static async Task<object> BuildInlinePdfPartAsync(string filePath, CancellationToken ct)
    {
        string fileName = Path.GetFileName(filePath);
        var pdfBytes = await File.ReadAllBytesAsync(filePath, ct);
        return new Dictionary<string, object>
        {
            ["type"] = "file",
            ["file"] = new Dictionary<string, string>
            {
                ["filename"] = fileName,
                ["file_data"] = $"data:application/pdf;base64,{Convert.ToBase64String(pdfBytes)}"
            }
        };
    }

    private static string CleanJsonBlock(string rawText)
    {
        // Bóc rào ```json``` và cắt dấu ngoặc đóng dư mà model đôi khi thêm ở đuôi JSON.
        return AiJsonText.ExtractBalancedJson(rawText);
    }

    /// <summary>Nạp prompt RIÊNG của CCCD/VBD-BN: ưu tiên file cạnh exe, sau đó embedded resource.</summary>
    private static string LoadPrompt()
    {
        if (_prompt != null) return _prompt;

        foreach (var p in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, PromptFileName),
                     Path.Combine(AppContext.BaseDirectory, "Temp", PromptFileName)
                 })
        {
            if (File.Exists(p))
            {
                try { return _prompt = File.ReadAllText(p, Encoding.UTF8); } catch { }
            }
        }

        var asm = typeof(CccdExtractService).Assembly;
        var resName = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith(PromptFileName, StringComparison.OrdinalIgnoreCase));
        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return _prompt = reader.ReadToEnd();
        }
        throw new Exception($"Không tìm thấy {PromptFileName} (đĩa lẫn embedded).");
    }
}
