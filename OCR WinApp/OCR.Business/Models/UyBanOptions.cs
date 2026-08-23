using System;
using System.IO;

namespace OCR.Business.Models;

/// <summary>Cấu hình pipeline "Đất Uỷ Ban": worker/retry/temp/template. AI profile dùng chung lấy từ AiProviderOptions.</summary>
public sealed class UyBanOptions
{
    public int Workers { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;

    /// <summary>Thư mục xuất Excel. Mặc định Documents\OCR WinApp\Output.</summary>
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "OCR WinApp", "Output");

    /// <summary>Đường dẫn Excel template (sheet "Data"), tương đối thư mục exe.</summary>
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_FormMau_v3.xlsx");

    /// <summary>Thư mục tạm ghi các PDF con (2 trang) đã tách trong lúc chạy (xoá &amp; tạo lại mỗi phiên).</summary>
    public string TempDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "uyban-temp");

    /// <summary>Tên thư mục con tạo trong thư mục đã chọn khi Export (chứa PDF con đã đổi tên).</summary>
    public string OutputSubFolder { get; set; } = "uyban-split";
}
