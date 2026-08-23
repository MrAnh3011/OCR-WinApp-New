using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;
using OCR.Business.Pdf;

namespace OCR.Business.NewGcn;

/// <summary>
/// Pipeline trích xuất "OCR GCN (New)" (port từ GcnOcrApp.NewExtractService): nhận file PDF/ảnh →
/// gửi nguyên PDF hoặc render ảnh → AI provider chung → NewGcnEnvelope JSON.
/// Provider/url/key/model lấy từ ai-provider.local.json khi build.
/// Bỏ phần tiền xử lý ảnh OpenCV (chỉ render ảnh thô khi cần).
/// </summary>
public sealed class NewGcnExtractService : INewGcnExtractService
{
    private const int MaxOutputTokens = 65536;

    // 3 lượt gọi cho mỗi file (1 lượt đầu + 2 lượt thử lại) theo yêu cầu nghiệp vụ "retry 3 lần".
    // Trước đây là 5 vì không có responseSchema nên model hay tự làm hỏng cú pháp số ở trường dạng mã
    // (VD ma_vach có 0 đứng đầu) và cứ lặp lại đúng lỗi đó; nay đã bật NewGcnResponseSchema khai báo các
    // trường ấy là STRING nên nguyên nhân gốc đã bị chặn, không cần tới 5 lượt nữa.
    private const int MaxJsonAttempts = 3;

    // Lượt gọi đầu dùng mức "medium" (rẻ hơn) cho mọi file; chỉ lượt rà soát lại do lệch số thửa
    // (RetryIfParcelCountMismatchAsync) mới nâng lên "high" — tránh trả phí thinking cao nhất cho
    // toàn bộ file trong khi phần lớn GCN đơn giản đã ra đúng kết quả ngay từ lượt đầu.
    private const string DefaultReasoningEffort = "medium";
    private const string EscalatedReasoningEffort = "high";

    private readonly IPdfRenderer _pdf;
    private readonly IAiModelClient _ai;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly NewGcnOptions _opt;
    private readonly NewGcnRunCacheService _cache;
    private static string? _prompt;

    public NewGcnExtractService(IPdfRenderer pdf, IAiModelClient ai, NewGcnOptions opt)
        : this(pdf, ai, DisabledGeminiFileApiService.Instance, opt, new NewGcnRunCacheService(opt.TempDir))
    {
    }

    public NewGcnExtractService(IPdfRenderer pdf, IAiModelClient ai, NewGcnOptions opt, NewGcnRunCacheService cache)
        : this(pdf, ai, DisabledGeminiFileApiService.Instance, opt, cache)
    {
    }

    public NewGcnExtractService(
        IPdfRenderer pdf,
        IAiModelClient ai,
        IGeminiFileApiService geminiFiles,
        NewGcnOptions opt,
        NewGcnRunCacheService cache)
    {
        _pdf = pdf;
        _ai = ai;
        _geminiFiles = geminiFiles;
        _opt = opt;
        _cache = cache;
    }

    public async Task<NewGcnEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        void Log(string m) => logCallback?.Invoke(m);

        string fileName = Path.GetFileName(filePath);
        cachePath ??= _cache.GetResponseCachePath(filePath);
        string cacheDir = Path.GetDirectoryName(cachePath)!;

        // 1. Cache.
        if (useCachedJson && IsFreshJsonCache(cachePath, filePath))
        {
            Log($"⚡ Phát hiện cache JSON: {Path.GetFileName(cachePath)}");
            try
            {
                var cached = JsonSerializer.Deserialize<NewGcnEnvelope>(
                    await File.ReadAllTextAsync(cachePath, ct),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cached != null)
                {
                    bool cacheHydrated = HydrateMissingSerial(cached, filePath, fileName, "cache");
                    if (CanUseCache(cached, filePath))
                    {
                        if (cacheHydrated)
                            await SaveCacheAsync(cached, cachePath, cacheDir, ct);
                        return cached;
                    }
                    Log("⚠️ Cache cũ thiếu hoặc lệch số thửa kiểm kê. Bỏ cache và quét lại file.");
                }
            }
            catch { }
        }
        else if (useCachedJson && File.Exists(cachePath))
        {
            Log("⚠️ Cache JSON cũ hơn file nguồn. Bỏ cache và quét lại file.");
        }

        var prompt = LoadPrompt();

        // 2. Chuẩn bị & gọi AI provider chung.
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        bool isPdf = ext == ".pdf";

        Func<string, string, CancellationToken, Task<string>> callAiAsync;
        if (isPdf && !_opt.OptimizeImages)
        {
            Log("📄 Gửi trực tiếp PDF đến AI provider (không tiền xử lý).");
            // Đọc byte PDF chuyển vào trong CallPdfDirectAsync (chỉ đọc khi producer CHƯA upload sẵn) —
            // tránh đọc lại nguyên PDF vô điều kiện khi uploadedFiles đã có (xem docs/superpowers/specs §4.4).
            callAiAsync = (requestPrompt, effort, token) =>
                CallPdfDirectAsync(requestPrompt, filePath, fileName, uploadedFiles, effort, token);
        }
        else
        {
            // Cần ảnh: render PDF→JPEG hoặc đọc file ảnh — bỏ qua nếu producer đã upload sẵn artifact:
            // producer dựng đúng từ cùng lời gọi RenderPagesJpegAsync/đọc file này (thứ tự trang là hợp
            // đồng producer/consumer), nên không cần rasterize lại cả tài liệu chỉ để biết số trang.
            List<byte[]> images = new();
            int pageCount;
            if (uploadedFiles is { Count: > 0 })
            {
                pageCount = uploadedFiles.Count;
            }
            else
            {
                if (isPdf)
                {
                    Log("📷 Render ảnh thô từ PDF...");
                    images = (await _pdf.RenderPagesJpegAsync(filePath, ct)).ToList();
                }
                else
                {
                    images.Add(await File.ReadAllBytesAsync(filePath, ct));
                }
                if (images.Count == 0) throw new Exception("Không có ảnh nào được kết xuất từ file.");
                pageCount = images.Count;
            }
            callAiAsync = (requestPrompt, effort, token) =>
                CallApiWithImagesAsync(requestPrompt, filePath, fileName, images, pageCount, ext, uploadedFiles, effort, token);
        }

        string responseStr = await callAiAsync(prompt, DefaultReasoningEffort, ct);

        // 3. Parse JSON.
        var envelope = ParseEnvelope(responseStr);
        envelope = await RetryIfParcelCountMismatchAsync(envelope, callAiAsync, prompt, Log, ct);
        envelope = await RetryIfOwnersMissingAsync(envelope, callAiAsync, prompt, Log, ct);
        envelope.ten_file = fileName;
        HydrateMissingSerial(envelope, filePath, fileName, "tên file");

        // 4. Lưu cache.
        await SaveCacheAsync(envelope, cachePath, cacheDir, ct);

        return envelope;
    }

    private static NewGcnEnvelope ParseEnvelope(string responseStr)
    {
        string cleanJson = CleanJsonBlock(responseStr);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleanJson);
        }
        catch (Exception ex)
        {
            throw new Exception($"Mô hình không trả về JSON đúng cấu trúc: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                int arrayLength = root.GetArrayLength();
                if (arrayLength == 0)
                    throw new Exception("Mô hình trả về mảng rỗng, không có dữ liệu giấy chứng nhận nào.");

                // Mảng ĐÚNG 1 phần tử chỉ là lỗi model bọc thừa object vào mảng — bóc ra dùng bình thường.
                // (Trước đây mọi array đều bị chặn, nên file 1 GCN báo lỗi tự mâu thuẫn "chứa 1 giấy
                // chứng nhận riêng biệt gộp trong 1 PDF".) Từ 2 phần tử mới thật sự là PDF bị ghép.
                if (arrayLength > 1) throw MultiGcnException(arrayLength);
                root = root[0];
            }

            NewGcnEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<NewGcnEnvelope>(
                    root.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                throw new Exception($"Mô hình không trả về JSON đúng cấu trúc: {ex.Message}");
            }

            if (envelope is null) throw new Exception("Không thể chuyển kết quả LLM sang cấu trúc GCN.");

            // Tín hiệu CHÍNH để phát hiện PDF ghép nhiều GCN: `responseSchema` ép đầu ra là object nên
            // model không còn đổi sang array được nữa — phải hỏi thẳng model đếm được bao nhiêu GCN.
            // Hoạt động ở mọi provider, kể cả khi schema bị bỏ (OpenRouter / provider trả 4xx).
            if (envelope.so_luong_gcn_trong_file is > 1)
                throw MultiGcnException(envelope.so_luong_gcn_trong_file.Value);

            return envelope;
        }
    }

    /// <summary>Lỗi hướng người dùng sang chức năng "Tách GCN" khi 1 PDF chứa nhiều GCN riêng biệt.</summary>
    private static Exception MultiGcnException(int count)
        => new Exception(
            $"File này chứa {count} giấy chứng nhận riêng biệt gộp trong 1 PDF (lỗi ghép khi scan). " +
            "Hãy dùng chức năng \"Tách GCN\" để tách file thành từng GCN riêng trước khi chạy lại OCR GCN (New).");

    private static async Task<NewGcnEnvelope> RetryIfParcelCountMismatchAsync(
        NewGcnEnvelope envelope,
        Func<string, string, CancellationToken, Task<string>> callAiAsync,
        string basePrompt,
        Action<string> log,
        CancellationToken ct)
    {
        int declaredCount = envelope.thong_tin_gcn.so_luong_thua_dat_doc_duoc ?? 0;
        int rowCount = envelope.danh_sach_dong?.Count ?? 0;
        if (declaredCount <= rowCount) return envelope;

        log($"⚠️ AI đếm được {declaredCount} thửa nhưng JSON chỉ có {rowCount} dòng. Rà soát lại thửa đất...");
        // Chỉ lượt rà soát này mới dùng mức reasoning cao nhất — lượt đầu (mismatch hiếm khi xảy ra
        // với file đơn giản) đã dùng DefaultReasoningEffort rẻ hơn ở trên.
        var retryEnvelope = ParseEnvelope(await callAiAsync(
            BuildParcelCountRetryPrompt(basePrompt, declaredCount, rowCount), EscalatedReasoningEffort, ct));
        int retryDeclaredCount = retryEnvelope.thong_tin_gcn.so_luong_thua_dat_doc_duoc ?? declaredCount;
        int retryRowCount = retryEnvelope.danh_sach_dong?.Count ?? 0;

        if (retryRowCount >= rowCount)
        {
            if (retryDeclaredCount > retryRowCount)
                AddParcelCountWarning(retryEnvelope, retryDeclaredCount, retryRowCount);
            return retryEnvelope;
        }

        AddParcelCountWarning(envelope, declaredCount, rowCount);
        return envelope;
    }

    /// <summary>
    /// Hậu kiểm CHỦ SỬ DỤNG: mục 1 của GCN luôn in người sử dụng đất, nên envelope không có chủ nào
    /// gần như chắc chắn là model đọc sót — trước đây lỗi này đi thẳng ra Excel mà không báo gì.
    /// Đọc lại đúng một lượt với mức reasoning cao nhất; vẫn không ra thì gắn `canh_bao` để người
    /// dùng thấy dòng cần rà soát thay vì lặng lẽ xuất ô trống.
    /// </summary>
    private static async Task<NewGcnEnvelope> RetryIfOwnersMissingAsync(
        NewGcnEnvelope envelope,
        Func<string, string, CancellationToken, Task<string>> callAiAsync,
        string basePrompt,
        Action<string> log,
        CancellationToken ct)
    {
        if (HasOwners(envelope)) return envelope;

        log("⚠️ Không đọc được chủ sử dụng. Đọc lại mục người sử dụng đất...");
        var retryEnvelope = ParseEnvelope(await callAiAsync(
            BuildOwnersRetryPrompt(basePrompt), EscalatedReasoningEffort, ct));
        if (HasOwners(retryEnvelope)) return retryEnvelope;

        AddOwnersWarning(envelope);
        return envelope;
    }

    private static bool HasOwners(NewGcnEnvelope envelope)
        => envelope.thong_tin_gcn.chu_su_dung_chi_tiet is { Count: > 0 } owners
           && owners.Any(o => !string.IsNullOrWhiteSpace(o?.ho_ten));

    private static string BuildOwnersRetryPrompt(string basePrompt)
        => basePrompt + """


---

YÊU CẦU ĐỌC LẠI NGƯỜI SỬ DỤNG ĐẤT

Ở lượt trước bạn trả về `thong_tin_gcn.chu_su_dung_chi_tiet` rỗng/không có họ tên. Mục
"1. Người sử dụng đất, chủ sở hữu tài sản gắn liền với đất" (mẫu cũ ghi "I. Người sử dụng đất...")
LUÔN được in trên giấy, nên đây gần như chắc chắn là đọc sót.

Bắt buộc:
1. Trả lại TOÀN BỘ JSON theo đúng schema ban đầu, không chỉ trả phần bị thiếu.
2. Đọc kỹ lại trang chủ sử dụng, kể cả chữ bị mờ/nghiêng/dấu mộc che, và điền đủ từng chủ vào
   `chu_su_dung_chi_tiet` (mỗi người một phần tử, `ho_ten` bắt buộc).
3. Chỉ khi thật sự KHÔNG có mục người sử dụng đất trên giấy mới để mảng rỗng, và phải ghi lý do
   vào `canh_bao`.
""";

    private static void AddOwnersWarning(NewGcnEnvelope envelope)
    {
        envelope.thong_tin_gcn.canh_bao ??= new List<string>();
        envelope.thong_tin_gcn.canh_bao.Add(
            "chu_su_dung_chi_tiet: không đọc được chủ sử dụng sau hậu kiểm; cần rà soát thủ công với GCN gốc.");
    }

    private static bool CanUseCache(NewGcnEnvelope envelope, string filePath)
    {
        int? declaredCount = envelope.thong_tin_gcn.so_luong_thua_dat_doc_duoc;
        int rowCount = envelope.danh_sach_dong?.Count ?? 0;
        bool hasSerial = !string.IsNullOrWhiteSpace(envelope.thong_tin_gcn.so_serial)
            || TryExtractSerial(Path.GetFileName(filePath), envelope.ten_file, Path.GetDirectoryName(filePath)).Length > 0;
        // Cache thiếu chủ sử dụng mà KHÔNG kèm cảnh báo là cache rác sinh ra trước khi schema có
        // propertyOrdering — dùng lại thì lỗi cũ sống mãi, phải quét lại file.
        bool ownersSettled = HasOwners(envelope) || envelope.thong_tin_gcn.canh_bao is { Count: > 0 };
        return hasSerial && ownersSettled && declaredCount.HasValue && declaredCount.Value <= rowCount;
    }

    private static bool IsFreshJsonCache(string cachePath, string filePath)
    {
        if (!File.Exists(cachePath) || !File.Exists(filePath)) return false;
        return File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(filePath);
    }

    private static async Task SaveCacheAsync(NewGcnEnvelope envelope, string cachePath, string cacheDir, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(envelope, options), ct);
        }
        catch { }
    }

    private static bool HydrateMissingSerial(NewGcnEnvelope envelope, string filePath, string fileName, string sourceLabel)
    {
        if (!string.IsNullOrWhiteSpace(envelope.thong_tin_gcn.so_serial)) return false;

        string serial = TryExtractSerial(fileName, envelope.ten_file, Path.GetFileNameWithoutExtension(filePath), Path.GetDirectoryName(filePath));
        if (string.IsNullOrWhiteSpace(serial)) return false;

        envelope.thong_tin_gcn.so_serial = serial;
        envelope.thong_tin_gcn.canh_bao ??= new List<string>();
        string warning = $"so_serial: được phục hồi từ {sourceLabel} ({serial}) do mô hình trả null; cần rà soát lại với PDF gốc.";
        if (!envelope.thong_tin_gcn.canh_bao.Any(w => w.Contains(warning, StringComparison.OrdinalIgnoreCase)))
            envelope.thong_tin_gcn.canh_bao.Add(warning);
        return true;
    }

    private static string TryExtractSerial(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            string value = candidate ?? "";
            foreach (Match match in Regex.Matches(value, @"(?<![A-Za-z0-9])([A-Za-z]{1,2})[\s_-]+(\d{6}|\d{8})(?!\d)"))
            {
                return $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}";
            }
        }

        return "";
    }

    private static string BuildParcelCountRetryPrompt(string basePrompt, int declaredCount, int rowCount)
        => basePrompt + $"""


---

YÊU CẦU RÀ SOÁT LẠI SỐ LƯỢNG THỬA ĐẤT

Ở lượt trước bạn tự kiểm kê được {declaredCount} thửa đất nhưng JSON `danh_sach_dong` chỉ có {rowCount} dòng.
Hãy đọc lại thật kỹ toàn bộ TRANG THỬA ĐẤT, đặc biệt các bảng nhiều dòng và các dòng bị mờ/dấu mộc che.

Bắt buộc:
1. Trả lại TOÀN BỘ JSON theo đúng schema ban đầu, không chỉ trả phần bị thiếu.
2. `thong_tin_gcn.so_luong_thua_dat_doc_duoc` phải là tổng số thửa đất thực sự đọc được trên GCN.
3. `danh_sach_dong.length` phải bằng `so_luong_thua_dat_doc_duoc`.
4. Nếu thấy một dòng thửa nhưng thiếu vài trường, vẫn tạo một phần tử trong `danh_sach_dong`, trường không đọc được để `null` và thêm `canh_bao`; tuyệt đối không bỏ dòng thửa đó.
""";

    private static void AddParcelCountWarning(NewGcnEnvelope envelope, int declaredCount, int rowCount)
    {
        envelope.thong_tin_gcn.canh_bao ??= new List<string>();
        envelope.thong_tin_gcn.canh_bao.Add(
            $"danh_sach_dong: AI kiểm kê được {declaredCount} thửa đất nhưng JSON chỉ có {rowCount} dòng sau hậu kiểm; cần rà soát thủ công.");
    }

    // ---------------------------------------------------------------- API ----
    private async Task<string> CallPdfDirectAsync(
        string prompt,
        string filePath,
        string fileName,
        IReadOnlyList<GeminiFileReference>? uploadedFiles,
        string reasoningEffort,
        CancellationToken ct)
    {
        var content = new List<object>
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = prompt }
        };
        if (uploadedFiles is { Count: > 0 })
        {
            // Producer đã đọc + upload PDF này rồi — KHÔNG đọc lại byte (xem docs/superpowers/specs §4.4).
            content.Add(ToGeminiFileContent(uploadedFiles[0]));
        }
        else
        {
            // Đường tương thích ngược (uploadedFiles rỗng) hoặc OpenRouter: vẫn cần byte thật.
            var pdfBytes = await File.ReadAllBytesAsync(filePath, ct);
            if (_geminiFiles.IsEnabled)
            {
                var file = await _geminiFiles.GetOrUploadAsync(
                    filePath, "source-pdf", fileName, "application/pdf", pdfBytes, ct);
                content.Add(ToGeminiFileContent(file));
            }
            else
            {
                content.Add(new Dictionary<string, object>
                {
                    ["type"] = "file",
                    ["file"] = new Dictionary<string, string>
                    {
                        ["filename"] = fileName,
                        ["file_data"] = $"data:application/pdf;base64,{Convert.ToBase64String(pdfBytes)}"
                    }
                });
            }
        }

        return await _ai.GenerateJsonAsync(
            content,
            maxAttempts: MaxJsonAttempts,
            maxTokens: MaxOutputTokens,
            temperature: 0,
            reasoningEnabled: true,
            reasoningEffort: reasoningEffort,
            timeoutSeconds: 300,
            responseSchema: NewGcnResponseSchema.Instance,
            // Lượt đầu dùng effort gốc (medium) cho rẻ; MỌI lượt thử lại tự nâng lên high vì đã có bằng
            // chứng lượt trước thất bại — gọi lại y nguyên request đã fail thì phần lớn sẽ fail tiếp.
            escalatedReasoningEffort: EscalatedReasoningEffort,
            ct: ct);
    }

    private async Task<string> CallApiWithImagesAsync(
        string prompt,
        string filePath,
        string fileName,
        List<byte[]> images,
        int pageCount,
        string sourceExtension,
        IReadOnlyList<GeminiFileReference>? uploadedFiles,
        string reasoningEffort,
        CancellationToken ct)
    {
        var mimeType = ImageMimeTypes.FromExtension(sourceExtension);
        var content = new List<object>
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = prompt }
        };

        // uploadedFiles.Count == pageCount luôn đúng theo cấu trúc: khi producer đã upload sẵn, pageCount
        // được gán TRỰC TIẾP từ uploadedFiles.Count (xem ProcessFileAsync) — không còn "images" độc lập
        // để đối chiếu, nên không cần (và không thể) assert lại ở đây.
        for (var index = 0; index < pageCount; index++)
        {
            if (uploadedFiles is { Count: > 0 })
            {
                // Producer đã upload đủ số trang, giữ đúng thứ tự.
                content.Add(ToGeminiFileContent(uploadedFiles[index]));
            }
            else if (_geminiFiles.IsEnabled)
            {
                var image = images[index];
                var file = await _geminiFiles.GetOrUploadAsync(
                    filePath,
                    $"page-{index + 1:D4}",
                    $"{Path.GetFileNameWithoutExtension(fileName)}-page-{index + 1:D4}{ImageMimeTypes.ToExtension(mimeType)}",
                    mimeType,
                    image,
                    ct);
                content.Add(ToGeminiFileContent(file));
            }
            else
            {
                var image = images[index];
                content.Add(new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, string>
                    {
                        ["url"] = $"data:{mimeType};base64,{Convert.ToBase64String(image)}"
                    }
                });
            }
        }

        return await _ai.GenerateJsonAsync(
            content,
            maxAttempts: MaxJsonAttempts,
            maxTokens: MaxOutputTokens,
            temperature: 0,
            reasoningEnabled: true,
            reasoningEffort: reasoningEffort,
            timeoutSeconds: 300,
            responseSchema: NewGcnResponseSchema.Instance,
            // Lượt đầu dùng effort gốc (medium) cho rẻ; MỌI lượt thử lại tự nâng lên high vì đã có bằng
            // chứng lượt trước thất bại — gọi lại y nguyên request đã fail thì phần lớn sẽ fail tiếp.
            escalatedReasoningEffort: EscalatedReasoningEffort,
            ct: ct);
    }

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

    private static string CleanJsonBlock(string rawText)
    {
        // Lớp 1: bóc rào ```json``` và cắt dấu ngoặc đóng dư mà model đôi khi thêm ở đuôi JSON.
        return AiJsonText.ExtractBalancedJson(rawText);
    }

    /// <summary>Nạp prompt: ưu tiên PROMPT_TRICH_XUAT_GCN.md cạnh exe, sau đó embedded resource.</summary>
    private static string LoadPrompt()
    {
        if (_prompt != null) return _prompt;

        foreach (var p in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "PROMPT_TRICH_XUAT_GCN.md"),
                     Path.Combine(AppContext.BaseDirectory, "Temp", "PROMPT_TRICH_XUAT_GCN.md")
                 })
        {
            if (File.Exists(p))
            {
                try { return _prompt = File.ReadAllText(p, Encoding.UTF8); } catch { }
            }
        }

        var asm = typeof(NewGcnExtractService).Assembly;
        var resName = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith("PROMPT_TRICH_XUAT_GCN.md", StringComparison.OrdinalIgnoreCase));
        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return _prompt = reader.ReadToEnd();
        }
        throw new Exception("Không tìm thấy PROMPT_TRICH_XUAT_GCN.md (đĩa lẫn embedded).");
    }
}
