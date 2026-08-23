using System;
using System.IO;

namespace OCR.Business.Models;

/// <summary>Cấu hình pipeline "Tách GCN" (port từ split_gcn.py): AI provider chung đọc PDF → cắt PDF theo khoảng trang.</summary>
public sealed class SplitGcnOptions
{
    public int Workers { get; set; } = 5;
    public int MaxRetries { get; set; } = 2;
    public int RequestTimeoutSeconds { get; set; } = 600;
    public string ReasoningEffort { get; set; } = "";
    public string PdfParserEngine { get; set; } = "";
    public bool EnableRouterMetadata { get; set; } = false;

    /// <summary>Thư mục gốc cache JSON/output, chia theo màn hình và khóa thư mục nguồn; chỉ xóa workspace sau Export thành công hoặc khi chọn Quét lại.</summary>
    public string TempDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "split-temp");

    /// <summary>Tên thư mục con tạo trong thư mục được chọn khi Export.</summary>
    public string OutputSubFolder { get; set; } = "split-file-gcn";
}
