namespace OCR.Business.Models;

/// <summary>
/// Cấu hình màn "Xóa trang trắng" — đọc từ mục <c>BlankPage</c> trong <c>appsettings.business.json</c>.
/// Bốn ngưỡng nhận diện port nguyên từ tool Python <c>Installer/BlankPageRemover</c>.
/// </summary>
public sealed class BlankPageOptions
{
    /// <summary>Số file PDF phân tích song song.</summary>
    public int Workers { get; set; } = 4;

    /// <summary>
    /// Độ phân giải render trang để phân tích (DPI). Cao hơn = chính xác hơn nhưng chậm hơn.
    /// Quy đổi sang hệ số render: <c>RenderDpi / 96</c> vì <c>PdfPage.Size</c> của Windows.Data.Pdf
    /// tính theo DIP (96 DIP = 1 inch) — cho ra ĐÚNG số điểm ảnh mà tool Python đạt được ở cùng DPI.
    /// </summary>
    public int RenderDpi { get; set; } = 100;

    /// <summary>
    /// Tỉ lệ viền bị cắt bỏ ở mỗi cạnh trước khi đo (0.03 = 3%).
    /// Mục đích: bỏ bóng đen và vệt mép giấy do máy scan tạo ra ở rìa trang.
    /// </summary>
    public double CropMarginRatio { get; set; } = 0.03;

    /// <summary>Điểm ảnh có độ xám NHỎ HƠN giá trị này (thang 0-255) được tính là "có mực".</summary>
    public int DarkPixelThreshold { get; set; } = 200;

    /// <summary>
    /// Tỉ lệ điểm ảnh "có mực" trên tổng số điểm ảnh vùng đo. Dưới ngưỡng này thì coi là trang trắng.
    /// 0.0015 = 0.15% diện tích trang.
    /// </summary>
    public double InkRatioThreshold { get; set; } = 0.0015;
}
