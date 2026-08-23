namespace OCR.Business.Models;

/// <summary>
/// Cấu hình màn "Đổi tên theo Serial" — đọc từ mục <c>SerialRename</c> trong <c>appsettings.business.json</c>.
/// KHÔNG dùng AI: serial đọc bằng OCR offline (<c>Windows.Media.Ocr</c>).
///
/// ⚠️ Bốn tham số render/crop BẮT BUỘC phải tinh chỉnh trên file scan thật: serial in màu ĐỎ trên nền
/// hoa văn nên độ chính xác phụ thuộc nhiều vào DPI và vùng cắt.
/// </summary>
public sealed class SerialRenameOptions
{
    /// <summary>Số file PDF đọc song song.</summary>
    public int Workers { get; set; } = 4;

    /// <summary>Độ phân giải render trang 1 trước khi OCR. Cao hơn = đọc chữ nhỏ tốt hơn, chậm hơn.</summary>
    public int RenderDpi { get; set; } = 300;

    /// <summary>Bề rộng vùng cắt tính từ mép phải, theo tỉ lệ chiều rộng trang (0.45 = 45%).</summary>
    public double CropRightRatio { get; set; } = 0.45;

    /// <summary>Chiều cao vùng cắt tính từ mép dưới, theo tỉ lệ chiều cao trang (0.25 = 25%).</summary>
    public double CropBottomRatio { get; set; } = 0.25;

    /// <summary>Chỉ nhận file .pdf có cụm này trong tên (so khớp bỏ dấu, không phân biệt hoa thường).</summary>
    public string GcnKeyword { get; set; } = "GCN";

    /// <summary>
    /// Ngôn ngữ OCR. Serial chỉ gồm chữ Latin + số nên gói tiếng Anh là đủ; thiếu gói thì
    /// <c>LocalOcrEngine</c> tự rơi về ngôn ngữ của người dùng.
    /// </summary>
    public string OcrLanguage { get; set; } = "en-US";
}
