using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;
using OCR.Business.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OCR.Business.Split;

public sealed class SplitGcnService : ISplitGcnService
{
    private readonly record struct PageRange(int From, int To);
    private readonly record struct PageInfo(int Page, string Type, string Serial);
    private readonly record struct GcnDocument(string Serial, PageRange Range);

    private readonly IAiModelClient _ai;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly IPdfRotationNormalizer _rotationNormalizer;
    private readonly SplitGcnOptions _opt;
    private static string? _standardPrompt;
    private static string? _newPrompt;
    private static string? _noGcnPrompt;

    public SplitGcnService(IAiModelClient ai, IPdfRotationNormalizer rotationNormalizer, SplitGcnOptions opt)
        : this(ai, DisabledGeminiFileApiService.Instance, rotationNormalizer, opt)
    {
    }

    public SplitGcnService(
        IAiModelClient ai,
        IGeminiFileApiService geminiFiles,
        IPdfRotationNormalizer rotationNormalizer,
        SplitGcnOptions opt)
    {
        _ai = ai;
        _geminiFiles = geminiFiles;
        _rotationNormalizer = rotationNormalizer;
        _opt = opt;
    }

    public async Task<SplitGcnResult> SplitAsync(
        string pdfPath,
        LabelAllocator allocator,
        bool normalizePageRotation = false,
        SplitGcnVariant variant = SplitGcnVariant.Standard,
        string? jsonPath = null,
        bool useCachedJson = false,
        CancellationToken ct = default,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        if (useCachedJson && !string.IsNullOrWhiteSpace(jsonPath) && IsFreshJsonCache(jsonPath, pdfPath))
        {
            // Lớp 1: cân bằng ngoặc để cache cũ bị dư dấu '}' vẫn dùng lại được thay vì phải quét lại.
            var cachedJson = AiJsonText.ExtractBalancedJson(await File.ReadAllTextAsync(jsonPath, ct));
            if (IsUsableJson(cachedJson))
                return await SplitFromJsonAsync(pdfPath, cachedJson, allocator, normalizePageRotation, variant, ct);
        }

        // 1. Dựng content (prompt + nguyên file PDF) rồi gọi AI provider dùng chung → nội dung JSON.
        var content = new List<object>
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = LoadPrompt(variant) }
        };
        if (uploadedFiles is { Count: > 0 })
        {
            // Producer đã đọc + upload PDF này rồi — KHÔNG đọc lại byte ở đây. Với lô 1400 file, đọc lại
            // vô điều kiện sẽ phá trần bộ nhớ "3 producer × full PDF" thành "(3 producer + 5 consumer) ×
            // full PDF" (xem docs/superpowers/specs §4.4).
            content.Add(ToGeminiFileContent(uploadedFiles[0]));
        }
        else if (_geminiFiles.IsEnabled)
        {
            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, ct);
            var file = await _geminiFiles.GetOrUploadAsync(
                pdfPath,
                "source-pdf",
                Path.GetFileName(pdfPath),
                "application/pdf",
                pdfBytes,
                ct);
            content.Add(ToGeminiFileContent(file));
        }
        else
        {
            // OpenRouter (tương thích ngược): vẫn cần byte thật để dựng data URL.
            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, ct);
            content.Add(new Dictionary<string, object>
            {
                ["type"] = "file",
                ["file"] = new Dictionary<string, string>
                {
                    ["filename"] = Path.GetFileName(pdfPath),
                    ["file_data"] = "data:application/pdf;base64," + Convert.ToBase64String(pdfBytes)
                }
            });
        }
        var apiContent = await _ai.GenerateJsonAsync(
            content, maxAttempts: Math.Max(1, _opt.MaxRetries) + 1,
            maxTokens: 65536, temperature: 0, reasoningEnabled: true,
            reasoningEffort: GetReasoningEffort(variant),
            pdfParserEngine: _opt.PdfParserEngine,
            enableRouterMetadata: _opt.EnableRouterMetadata,
            timeoutSeconds: _opt.RequestTimeoutSeconds,
            responseSchema: BuildResponseSchema(variant), ct: ct);

        // 2. Lưu JSON kết quả của file PDF này vào thư mục temp (đặt tên theo file nguồn).
        //    File JSON chỉ nằm trong temp, KHÔNG được copy ra khi Export.
        //    Lớp 1: cân bằng ngoặc trước khi ghi để cache luôn sạch (không lưu dấu '}' dư).
        var cleanJson = AiJsonText.ExtractBalancedJson(apiContent);
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath, cleanJson, Encoding.UTF8, ct);
        }

        return await SplitFromJsonAsync(pdfPath, cleanJson, allocator, normalizePageRotation, variant, ct);
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

    private async Task<SplitGcnResult> SplitFromJsonAsync(
        string pdfPath,
        string cleanJson,
        LabelAllocator allocator,
        bool normalizePageRotation,
        SplitGcnVariant variant,
        CancellationToken ct)
    {
        List<JsonElement> items;
        try
        {
            items = ExtractDocuments(cleanJson);
        }
        catch
        {
            items = new List<JsonElement>();
        }
        items = RepairDocumentsFromPages(cleanJson, items);
        if (items.Count == 0)
        {
            try { items = ExtractDocuments(cleanJson); }
            catch { items = new List<JsonElement>(); }
        }

        // 4. Cắt PDF theo từng phần tử. Nếu người dùng bật, OCR local xoay từng trang PDF con về cùng hướng đọc.
        return await SplitFromResultAsync(pdfPath, items, allocator, normalizePageRotation, variant, ct);
    }

    private static bool IsUsableJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ||
                   (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("documents", out var documents) &&
                    documents.ValueKind == JsonValueKind.Array);
        }
        catch { return false; }
    }

    private static bool IsFreshJsonCache(string jsonPath, string sourceFile)
    {
        if (!File.Exists(sourceFile) || !File.Exists(jsonPath)) return false;
        return File.GetLastWriteTimeUtc(jsonPath) >= File.GetLastWriteTimeUtc(sourceFile);
    }

    /// <summary>
    /// Lấy mảng các bộ GCN từ phản hồi. Ưu tiên object { "pages":[...], "documents":[...] } (định dạng mới);
    /// fallback về mảng JSON top-level (tương thích định dạng cũ). Có thể bị bọc ```json ... ```.
    /// </summary>
    private static List<JsonElement> ExtractDocuments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new Exception("Phản hồi rỗng.");

        var fenced = Regex.Match(text, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline);
        var candidate = (fenced.Success ? fenced.Groups[1].Value : text).Trim();

        int objStart = candidate.IndexOf('{');
        int objEnd = candidate.LastIndexOf('}');
        int arrStart = candidate.IndexOf('[');
        int arrEnd = candidate.LastIndexOf(']');

        // Định dạng mới: object bao ngoài (dấu { đứng trước dấu [ đầu tiên) → đọc "documents".
        if (objStart != -1 && objEnd > objStart && (arrStart == -1 || objStart < arrStart))
        {
            using var doc = JsonDocument.Parse(candidate.Substring(objStart, objEnd - objStart + 1));
            if (doc.RootElement.TryGetProperty("documents", out var docs) && docs.ValueKind == JsonValueKind.Array)
                return docs.EnumerateArray().Select(e => e.Clone()).ToList();
            throw new Exception("Phản hồi JSON không có mảng 'documents'.");
        }

        // Fallback: mảng JSON top-level (định dạng cũ).
        if (arrStart != -1 && arrEnd > arrStart)
        {
            using var doc = JsonDocument.Parse(candidate.Substring(arrStart, arrEnd - arrStart + 1));
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        throw new Exception("Không tìm thấy 'documents' hoặc mảng JSON trong phản hồi.");
    }

    /// <summary>
    /// Dùng bảng pages của model để chuẩn hoá lại documents theo từng bộ hồ sơ liên tiếp:
    /// [GCN...][GT/GTK] [GCN...][GT/GTK]... Output vẫn là danh sách GCN phẳng như schema cũ.
    /// </summary>
    private static List<JsonElement> RepairDocumentsFromPages(string json, List<JsonElement> items)
    {
        if (!TryExtractPages(json, out var pages)) return items;

        var gcns = BuildGcnDocuments(pages);
        if (gcns.Count == 0) return items;

        var parcelCountHints = BuildParcelCountHints(items);
        int lastPage = pages.Max(p => p.Page);
        var repaired = new List<JsonElement>(gcns.Count);

        for (int i = 0; i < gcns.Count;)
        {
            int blockStart = i;
            int blockEnd = i;

            // Nhiều GCN nằm liền nhau ở đầu cùng một bộ hồ sơ dùng chung khối GT/GTK phía sau.
            while (blockEnd + 1 < gcns.Count &&
                   gcns[blockEnd + 1].Range.From == gcns[blockEnd].Range.To + 1)
            {
                blockEnd++;
            }

            int tailStart = gcns[blockEnd].Range.To + 1;
            int tailEnd = blockEnd + 1 < gcns.Count
                ? gcns[blockEnd + 1].Range.From - 1
                : lastPage;

            var (gt, gtk) = GetTailRanges(pages, tailStart, tailEnd);
            for (int docIndex = blockStart; docIndex <= blockEnd; docIndex++)
            {
                int parcelCount = DequeueParcelCount(parcelCountHints, gcns[docIndex]);
                repaired.Add(CreateDocumentElement(gcns[docIndex], gt, gtk, parcelCount));
            }

            i = blockEnd + 1;
        }

        return repaired.Count > 0 ? repaired : items;
    }

    private static Dictionary<string, Queue<int>> BuildParcelCountHints(List<JsonElement> items)
    {
        var hints = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var serial = Regex.Replace(GetString(item, "name").Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(serial)) continue;

            int parcelCount = GetParcelCount(item);
            EnqueueParcelCount(hints, serial, parcelCount);

            if (TryGetRange(item, "serial_GCN.pdf", out int from, out int to))
                EnqueueParcelCount(hints, MakeParcelHintKey(serial, from, to), parcelCount);
        }

        return hints;
    }

    private static void EnqueueParcelCount(Dictionary<string, Queue<int>> hints, string key, int parcelCount)
    {
        if (!hints.TryGetValue(key, out var queue))
        {
            queue = new Queue<int>();
            hints[key] = queue;
        }
        queue.Enqueue(parcelCount);
    }

    private static int DequeueParcelCount(Dictionary<string, Queue<int>> hints, GcnDocument gcn)
    {
        foreach (var key in new[]
                 {
                     MakeParcelHintKey(gcn.Serial, gcn.Range.From, gcn.Range.To),
                     gcn.Serial
                 })
        {
            if (hints.TryGetValue(key, out var queue) && queue.Count > 0)
                return Math.Max(1, queue.Dequeue());
        }

        return 1;
    }

    private static string MakeParcelHintKey(string serial, int from, int to)
        => $"{serial}|{from}|{to}";

    private static bool TryExtractPages(string json, out List<PageInfo> pages)
    {
        pages = new List<PageInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var pageEl in pagesEl.EnumerateArray())
            {
                int page = GetPageNumber(pageEl);
                if (page <= 0) continue;

                string type = GetString(pageEl, "type").Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(type)) continue;

                string serial = Regex.Replace(GetString(pageEl, "serial").Trim(), @"\s+", " ");
                pages.Add(new PageInfo(page, type, serial));
            }

            pages = pages
                .GroupBy(p => p.Page)
                .Select(g => g.First())
                .OrderBy(p => p.Page)
                .ToList();

            return pages.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<GcnDocument> BuildGcnDocuments(IReadOnlyList<PageInfo> pages)
    {
        var starts = pages
            .Where(p => p.Type == "GCN" && !string.IsNullOrWhiteSpace(p.Serial))
            .OrderBy(p => p.Page)
            .ToList();

        var gcns = new List<GcnDocument>(starts.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            int nextStart = i + 1 < starts.Count ? starts[i + 1].Page : int.MaxValue;
            int end = starts[i].Page;

            foreach (var page in pages.Where(p => p.Page > starts[i].Page && p.Page < nextStart))
            {
                if (page.Type != "GCN") break;
                end = page.Page;
            }

            gcns.Add(new GcnDocument(starts[i].Serial, new PageRange(starts[i].Page, end)));
        }

        return gcns;
    }

    private static (PageRange? Gt, PageRange? Gtk) GetTailRanges(IReadOnlyList<PageInfo> pages, int tailStart, int tailEnd)
    {
        if (tailStart <= 0 || tailEnd < tailStart)
            return (null, null);

        var tailPages = pages
            .Where(p => p.Page >= tailStart && p.Page <= tailEnd)
            .OrderBy(p => p.Page)
            .ToList();

        int firstPlyk = tailPages.FirstOrDefault(p => p.Type == "PLYK").Page;
        if (firstPlyk > 0)
        {
            PageRange? gt = tailStart <= firstPlyk - 1 ? new PageRange(tailStart, firstPlyk - 1) : null;
            PageRange? gtk = firstPlyk <= tailEnd ? new PageRange(firstPlyk, tailEnd) : null;
            return (gt, gtk);
        }

        int lastKnownGt = tailPages
            .Where(p => IsProcedureType(p.Type))
            .Select(p => p.Page)
            .DefaultIfEmpty(0)
            .Max();

        if (lastKnownGt > 0)
        {
            var gt = new PageRange(tailStart, lastKnownGt);
            PageRange? gtk = lastKnownGt + 1 <= tailEnd ? new PageRange(lastKnownGt + 1, tailEnd) : null;
            return (gt, gtk);
        }

        return (null, new PageRange(tailStart, tailEnd));
    }

    private static bool IsProcedureType(string type)
        => type is "DON" or "DSTD" or "TKLP" or "BBXD" or "BKCT";

    private static int GetParcelCount(JsonElement item)
    {
        if (TryReadInt(item, "parcel_count", out int parcelCount)) return Math.Max(1, parcelCount);
        if (TryReadInt(item, "parcelCount", out parcelCount)) return Math.Max(1, parcelCount);
        return 1;
    }

    private static JsonElement CreateDocumentElement(GcnDocument gcn, PageRange? gt, PageRange? gtk, int parcelCount)
    {
        var node = new JsonObject
        {
            ["name"] = gcn.Serial,
            ["parcel_count"] = Math.Max(1, parcelCount),
            ["serial_GCN.pdf"] = ToRangeNode(gcn.Range),
            ["serial_GT.pdf"] = gt.HasValue ? ToRangeNode(gt.Value) : null,
            ["serial_GTK.pdf"] = gtk.HasValue ? ToRangeNode(gtk.Value) : null
        };

        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static JsonObject ToRangeNode(PageRange range) => new()
    {
        ["from"] = range.From,
        ["to"] = range.To
    };

    private static int GetPageNumber(JsonElement el)
        => TryReadInt(el, "page", out int page) ? page : 0;

    // ---------------------------------------------------------------- Cut ----
    private async Task<SplitGcnResult> SplitFromResultAsync(
        string pdfPath,
        List<JsonElement> items,
        LabelAllocator allocator,
        bool normalizePageRotation,
        SplitGcnVariant variant,
        CancellationToken ct)
    {
        using var src = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        int made = 0;
        int completeSetCount = 0;
        int rotated = 0;
        var missingFileFolders = new List<string>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            var serial = GetDocumentLabel(item, variant);
            int parcelCount = variant is SplitGcnVariant.New or SplitGcnVariant.NoGcn ? 1 : GetParcelCount(item);
            var outputs = allocator.AllocateGroup(serial, parcelCount);
            var filesCreatedByOutput = new int[outputs.Count];

            var targets = GetTargets(variant);

            foreach (var (suffix, key) in targets)
            {
                if (!TryGetRange(item, key, out int from, out int to)) continue;

                var primary = outputs[0];
                var dest = Path.Combine(primary.Dir, $"{primary.Label}-{suffix}.pdf");

                // Xoay thẳng trang chỉ áp dụng cho GCN và GT; GTK giữ nguyên hướng gốc.
                var rotateThis = normalizePageRotation && !suffix.Equals("GTK", StringComparison.OrdinalIgnoreCase);
                var result = await WriteSliceAsync(src, from, to, dest, rotateThis, ct);
                if (result.Created)
                {
                    made++;
                    filesCreatedByOutput[0]++;
                    rotated += result.PagesRotated;

                    for (int outputIndex = 1; outputIndex < outputs.Count; outputIndex++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var clone = outputs[outputIndex];
                        var cloneDest = Path.Combine(clone.Dir, $"{clone.Label}-{suffix}.pdf");
                        await Task.Run(() => File.Copy(dest, cloneDest, overwrite: true), ct);
                        made++;
                        filesCreatedByOutput[outputIndex]++;
                    }
                }
            }

            completeSetCount += filesCreatedByOutput.Count(count => count == targets.Length);

            if (variant == SplitGcnVariant.New)
            {
                for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    if (filesCreatedByOutput[outputIndex] < 3)
                        missingFileFolders.Add(outputs[outputIndex].Label);
                }
            }
        }

        return new SplitGcnResult
        {
            GcnCount = items.Count,
            FilesCreated = made,
            CompleteSetCount = completeSetCount,
            PagesRotated = rotated,
            MissingFileFolders = missingFileFolders
        };
    }

    /// <summary>Cắt khoảng trang [from, to] (1-index, inclusive) ra file đích.</summary>
    private async Task<(bool Created, int PagesRotated)> WriteSliceAsync(
        PdfDocument src,
        int from,
        int to,
        string destPath,
        bool normalizePageRotation,
        CancellationToken ct)
    {
        int total = src.PageCount;
        int start = Math.Max(1, from);
        int end = Math.Min(total, to);
        if (end < start) return (false, 0);

        await Task.Run(() =>
        {
            using var outDoc = new PdfDocument();
            for (int i = start; i <= end; i++)
                outDoc.AddPage(src.Pages[i - 1]);
            outDoc.Save(destPath);
        }, ct);

        int rotated = normalizePageRotation
            ? await _rotationNormalizer.NormalizeAsync(destPath, ct)
            : 0;

        return (true, rotated);
    }

    private static bool TryGetRange(JsonElement item, string key, out int from, out int to)
    {
        from = to = 0;
        if (!item.TryGetProperty(key, out var rng) || rng.ValueKind != JsonValueKind.Object) return false;
        if (!TryReadInt(rng, "from", out from) || !TryReadInt(rng, "to", out to)) return false;
        return true;
    }

    private static bool TryReadInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value)) return true;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out value)) return true;
        return false;
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static (string Suffix, string Key)[] GetTargets(SplitGcnVariant variant)
        => variant == SplitGcnVariant.NoGcn
            ? new[] { ("GT", "serial_GT.pdf"), ("GTK", "serial_GTK.pdf") }
            : new[] { ("GCN", "serial_GCN.pdf"), ("GT", "serial_GT.pdf"), ("GTK", "serial_GTK.pdf") };

    private static string GetDocumentLabel(JsonElement item, SplitGcnVariant variant)
    {
        if (variant != SplitGcnVariant.NoGcn)
            return Sanitize(GetString(item, "name"));

        var sheet = GetFirstString(item, "sheet_number", "so_to", "so_to_ban_do", "map_sheet");
        var parcel = GetFirstString(item, "parcel_number", "so_thua", "so_thua_dat", "parcel");
        if (!string.IsNullOrWhiteSpace(sheet) && !string.IsNullOrWhiteSpace(parcel))
            return Sanitize($"{sheet}-{parcel}");

        return Sanitize(GetString(item, "name").Replace('_', '-'));
    }

    private static string GetFirstString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(el, name).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    /// <summary>Chuẩn hoá tên thư mục: bỏ ký tự cấm Windows, gộp khoảng trắng, giữ 1 dấu cách.</summary>
    private static string Sanitize(string? name)
    {
        name = (name ?? "").Trim();
        name = Regex.Replace(name, @"[<>:""/\\|?*]", "");
        name = Regex.Replace(name, @"\s+", " ");
        return string.IsNullOrEmpty(name) ? "UNKNOWN" : name;
    }

    /// <summary>
    /// LỚP 2 — Schema đầu ra (định dạng Google AI Studio, type IN HOA) cho CẢ BA màn Tách GCN
    /// (Standard/New/NoGcn) để bật constrained decoding, tránh model đóng thừa ngoặc. Chỉ Gemini dùng;
    /// nếu provider từ chối schema thì client tự thử lại không schema (không hồi quy).
    /// </summary>
    private static object BuildResponseSchema(SplitGcnVariant variant)
    {
        static Dictionary<string, object> Str() => new() { ["type"] = "STRING" };
        static Dictionary<string, object> Int() => new() { ["type"] = "INTEGER" };

        static Dictionary<string, object> Range(bool nullable)
        {
            var range = new Dictionary<string, object>
            {
                ["type"] = "OBJECT",
                ["properties"] = new Dictionary<string, object> { ["from"] = Int(), ["to"] = Int() },
                ["required"] = new[] { "from", "to" }
            };
            if (nullable) range["nullable"] = true;
            return range;
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> props, string[] required) => new()
        {
            ["type"] = "OBJECT",
            ["properties"] = props,
            ["required"] = required
        };

        static Dictionary<string, object> Arr(object item) => new()
        {
            ["type"] = "ARRAY",
            ["items"] = item
        };

        Dictionary<string, object> pageItem, docItem;

        if (variant == SplitGcnVariant.NoGcn)
        {
            // Không GCN: pages có sheet_number/parcel_number, documents không có serial_GCN.pdf.
            pageItem = Obj(new Dictionary<string, object>
            {
                ["page"] = Int(),
                ["type"] = Str(),
                ["sheet_number"] = Str(),
                ["parcel_number"] = Str()
            }, new[] { "page", "type", "sheet_number", "parcel_number" });

            docItem = Obj(new Dictionary<string, object>
            {
                ["name"] = Str(),
                ["sheet_number"] = Str(),
                ["parcel_number"] = Str(),
                ["serial_GT.pdf"] = Range(nullable: true),
                ["serial_GTK.pdf"] = Range(nullable: true)
            }, new[] { "name", "sheet_number", "parcel_number" });
        }
        else
        {
            // Có GCN (Standard/New): pages có serial, documents bắt buộc serial_GCN.pdf.
            pageItem = Obj(new Dictionary<string, object>
            {
                ["page"] = Int(),
                ["type"] = Str(),
                ["serial"] = Str()
            }, new[] { "page", "type", "serial" });

            var docProps = new Dictionary<string, object>
            {
                ["name"] = Str(),
                ["serial_GCN.pdf"] = Range(nullable: false),
                ["serial_GT.pdf"] = Range(nullable: true),
                ["serial_GTK.pdf"] = Range(nullable: true)
            };
            if (variant == SplitGcnVariant.Standard)
                docProps["parcel_count"] = Int();

            docItem = Obj(docProps, new[] { "name", "serial_GCN.pdf" });
        }

        return Obj(new Dictionary<string, object>
        {
            ["pages"] = Arr(pageItem),
            ["documents"] = Arr(docItem)
        }, new[] { "pages", "documents" });
    }

    /// <summary>Nạp prompt: ưu tiên prompt_split_gcn.md cạnh exe, sau đó embedded resource.</summary>
    private string GetReasoningEffort(SplitGcnVariant _)
        => string.IsNullOrWhiteSpace(_opt.ReasoningEffort)
            ? "medium"
            : _opt.ReasoningEffort.Trim();

    private static string LoadPrompt(SplitGcnVariant variant)
    {
        var promptFileName = variant switch
        {
            SplitGcnVariant.New => "prompt_split_gcn_new.md",
            SplitGcnVariant.NoGcn => "prompt_split_gcn_no_gcn.md",
            _ => "prompt_split_gcn.md"
        };

        if (variant == SplitGcnVariant.New && _newPrompt != null) return _newPrompt;
        if (variant == SplitGcnVariant.NoGcn && _noGcnPrompt != null) return _noGcnPrompt;
        if (variant == SplitGcnVariant.Standard && _standardPrompt != null) return _standardPrompt;

        var diskPath = Path.Combine(AppContext.BaseDirectory, promptFileName);
        if (File.Exists(diskPath))
        {
            try { return SetPromptCache(variant, File.ReadAllText(diskPath, Encoding.UTF8)); } catch { }
        }

        var asm = typeof(SplitGcnService).Assembly;
        var resName = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith(promptFileName, StringComparison.OrdinalIgnoreCase));
        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return SetPromptCache(variant, reader.ReadToEnd());
        }
        throw new Exception($"Không tìm thấy {promptFileName} (đĩa lẫn embedded).");
    }

    private static string SetPromptCache(SplitGcnVariant variant, string prompt)
    {
        if (variant == SplitGcnVariant.New)
            return _newPrompt = prompt;
        if (variant == SplitGcnVariant.NoGcn)
            return _noGcnPrompt = prompt;

        return _standardPrompt = prompt;
    }
}
