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

namespace OCR.Business.UyBan;

/// <summary>
/// Pipeline "Đất Uỷ Ban" (port từ process_and_ocr_api.py): render trang 1 → ảnh (KHÔNG tăng cường ảnh)
/// → gọi AI provider chung với prompt → JSON → UyBanRecord.
/// </summary>
public sealed class UyBanExtractService : IUyBanExtractService
{
    private readonly IPdfRenderer _pdf;
    private readonly IAiModelClient _ai;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly UyBanOptions _opt;
    private static string? _prompt;

    public UyBanExtractService(IPdfRenderer pdf, IAiModelClient ai, UyBanOptions opt)
        : this(pdf, ai, DisabledGeminiFileApiService.Instance, opt)
    {
    }

    public UyBanExtractService(
        IPdfRenderer pdf,
        IAiModelClient ai,
        IGeminiFileApiService geminiFiles,
        UyBanOptions opt)
    {
        _pdf = pdf;
        _ai = ai;
        _geminiFiles = geminiFiles;
        _opt = opt;
    }

    public async Task<UyBanRecord> ExtractAsync(
        string pdfPath,
        CancellationToken ct = default,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        // 2. Dựng content (prompt + ảnh) rồi gọi AI provider dùng chung.
        var content = new List<object>
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = LoadPrompt() }
        };
        if (uploadedFiles is { Count: > 0 })
        {
            // Producer đã render trang 1 + upload rồi — KHÔNG render lại. Đây là màn mà producer CHỈ có
            // mỗi việc render trang 1; render lại vô điều kiện ở đây là thụt lùi CPU so với main (trước
            // render 1 lần, nếu không có nhánh này sẽ thành 2 lần).
            var preUploaded = uploadedFiles[0];
            content.Add(new Dictionary<string, object>
            {
                ["type"] = "file_uri",
                ["file_uri"] = new Dictionary<string, string>
                {
                    ["mime_type"] = preUploaded.MimeType,
                    ["file_uri"] = preUploaded.Uri
                }
            });
        }
        else
        {
            // 1. Render trang 1 PDF → ảnh JPEG (bỏ qua bước tăng cường ảnh) — chỉ khi producer CHƯA
            // upload sẵn (đường tương thích ngược / OpenRouter vẫn cần byte thật).
            var jpeg = await _pdf.RenderPageJpegAsync(pdfPath, 0, ct);
            if (_geminiFiles.IsEnabled)
            {
                var file = await _geminiFiles.GetOrUploadAsync(
                    pdfPath,
                    "page-0001",
                    Path.GetFileNameWithoutExtension(pdfPath) + "-page-0001.jpg",
                    "image/jpeg",
                    jpeg,
                    ct);
                content.Add(new Dictionary<string, object>
                {
                    ["type"] = "file_uri",
                    ["file_uri"] = new Dictionary<string, string>
                    {
                        ["mime_type"] = file.MimeType,
                        ["file_uri"] = file.Uri
                    }
                });
            }
            else
            {
                content.Add(new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, string>
                    {
                        ["url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}"
                    }
                });
            }
        }
        var json = await _ai.GenerateJsonAsync(
            content, maxAttempts: _opt.MaxRetries, maxTokens: 8192, temperature: 0, timeoutSeconds: 180, ct: ct);

        // 3. Parse JSON → record.
        var resp = ParseResponse(json);
        return new UyBanRecord
        {
            FilePath = pdfPath,
            TrangThai = "Hoàn thành",
            HoVaTen = resp.HO_VA_TEN_2,
            DiaChi4 = resp.DIA_CHI_4,
            ThuaDatSo = resp.THUA_DAT_SO,
            ToBanDoSo = resp.TO_BAN_DO_SO,
            DiaChi5 = resp.DIA_CHI_5,
            DienTich6 = resp.DIEN_TICH_6,
            SuDungChung = resp.SU_DUNG_CHUNG,
            SuDungRieng = resp.SU_DUNG_RIENG,
            MucDich7 = resp.SU_DUNG_VAO_MUC_DICH_7,
            ThoiHan8 = resp.THOI_HAN_SU_DUNG_DAT_8
        };
    }

    private static UyBanExtractResponse ParseResponse(string text)
    {
        text = (text ?? "").Trim();
        text = Regex.Replace(text, @"^```(?:json)?|```$", "", RegexOptions.Multiline).Trim();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            return JsonSerializer.Deserialize<UyBanExtractResponse>(text, options) ?? new();
        }
        catch { }

        int s = text.IndexOf('{'), e = text.LastIndexOf('}');
        if (s != -1 && e > s)
        {
            try { return JsonSerializer.Deserialize<UyBanExtractResponse>(text.Substring(s, e - s + 1), options) ?? new(); }
            catch { }
        }
        throw new Exception("Không parse được JSON từ phản hồi API.");
    }

    /// <summary>Nạp prompt: ưu tiên prompt_uyban.md cạnh exe, sau đó embedded resource.</summary>
    private static string LoadPrompt()
    {
        if (_prompt != null) return _prompt;

        var diskPath = Path.Combine(AppContext.BaseDirectory, "prompt_uyban.md");
        if (File.Exists(diskPath))
        {
            try { return _prompt = File.ReadAllText(diskPath, Encoding.UTF8); } catch { }
        }

        var asm = typeof(UyBanExtractService).Assembly;
        var resName = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith("prompt_uyban.md", StringComparison.OrdinalIgnoreCase));
        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return _prompt = reader.ReadToEnd();
        }
        throw new Exception("Không tìm thấy prompt_uyban.md (đĩa lẫn embedded).");
    }
}

/// <summary>Schema JSON trả về (Mẫu số 15).</summary>
internal sealed class UyBanExtractResponse
{
    public string HO_VA_TEN_2 { get; set; } = "";
    public string DIA_CHI_4 { get; set; } = "";
    public string THUA_DAT_SO { get; set; } = "";
    public string TO_BAN_DO_SO { get; set; } = "";
    public string DIA_CHI_5 { get; set; } = "";
    public string DIEN_TICH_6 { get; set; } = "";
    public string SU_DUNG_CHUNG { get; set; } = "";
    public string SU_DUNG_RIENG { get; set; } = "";
    public string SU_DUNG_VAO_MUC_DICH_7 { get; set; } = "";
    public string THOI_HAN_SU_DUNG_DAT_8 { get; set; } = "";
}
