namespace OCR.Business.BlankPage;

/// <summary>Kết luận của bước phân tích cho MỘT file PDF nguồn.</summary>
public enum BlankPageStatus
{
    /// <summary>Không có trang trắng — Export copy nguyên file gốc.</summary>
    NoBlank,

    /// <summary>Có trang trắng — Export ghi bản mới đã bỏ các trang đó.</summary>
    Removed,

    /// <summary>
    /// TẤT CẢ các trang đều bị coi là trắng. Export GIỮ NGUYÊN file gốc, không xoá gì:
    /// nhiều khả năng ngưỡng đặt sai hoặc bản scan hỏng, xoá hết sẽ mất dữ liệu.
    /// </summary>
    AllBlank,

    /// <summary>PDF hỏng / có mật khẩu / không có trang nào — Export copy nguyên file gốc.</summary>
    OpenError
}

/// <summary>Kết quả phân tích một file PDF nguồn.</summary>
public sealed class BlankPageScan
{
    public required string SourcePath { get; init; }

    /// <summary>Đường dẫn tương đối tính từ thư mục nguồn đã chọn.</summary>
    public required string RelativePath { get; init; }

    public BlankPageStatus Status { get; init; }

    public int TotalPages { get; init; }

    /// <summary>Chỉ số các trang trắng, đếm từ 0.</summary>
    public IReadOnlyList<int> BlankPageIndexes { get; init; } = Array.Empty<int>();

    /// <summary>Nội dung lỗi hoặc ghi chú thêm, đưa thẳng vào cột ghi chú của báo cáo CSV.</summary>
    public string Note { get; init; } = "";

    /// <summary>Số trang Export sẽ thực sự bỏ đi (chỉ khác 0 khi <see cref="Status"/> = Removed).</summary>
    public int RemovedPageCount => Status == BlankPageStatus.Removed ? BlankPageIndexes.Count : 0;

    /// <summary>
    /// File cần người dùng xem lại bằng tay — đây chính là tập mà nút "File lỗi" copy ra:
    /// PDF hỏng và PDF bị coi là trắng toàn bộ.
    /// </summary>
    public bool NeedsReview => Status is BlankPageStatus.AllBlank or BlankPageStatus.OpenError;

    /// <summary>Số thứ tự các trang trắng, đánh số từ 1 theo file gốc (để hiển thị và ghi CSV).</summary>
    public string DescribeBlankPages()
        => string.Join(", ", BlankPageIndexes.Select(i => (i + 1).ToString()));
}

/// <summary>Đầu vào của bước dựng cây kết quả.</summary>
/// <param name="SourceRoot">Thư mục nguồn người dùng đã chọn.</param>
/// <param name="DestinationRoot">Thư mục lưu kết quả người dùng chọn lúc Export.</param>
/// <param name="PdfScans">Kết quả phân tích từng file PDF.</param>
/// <param name="OtherFiles">
/// Đường dẫn tuyệt đối các file KHÔNG phải PDF — copy nguyên để cây đích khớp 100% cây nguồn.
/// </param>
public sealed record BlankPageExportRequest(
    string SourceRoot,
    string DestinationRoot,
    IReadOnlyList<BlankPageScan> PdfScans,
    IReadOnlyList<string> OtherFiles);

/// <summary>Thống kê sau khi dựng xong cây kết quả.</summary>
public sealed class BlankPageExportResult
{
    /// <summary>Số PDF đã ghi bản mới sau khi bỏ trang trắng.</summary>
    public int PdfCleaned { get; set; }

    /// <summary>Số PDF copy nguyên (không có trang trắng / trắng toàn bộ / hỏng).</summary>
    public int PdfCopied { get; set; }

    /// <summary>Số file không phải PDF đã copy.</summary>
    public int OtherCopied { get; set; }

    /// <summary>Tổng số trang đã bỏ đi.</summary>
    public int PagesRemoved { get; set; }

    /// <summary>Số file không copy/ghi được (đã ghi lý do vào CSV).</summary>
    public int Failed { get; set; }

    public string ReportPath { get; set; } = "";
}
