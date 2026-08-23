namespace OCR.Business.SerialRename;

/// <summary>Kết quả đọc serial của MỘT file GCN.</summary>
public enum SerialReadStatus
{
    /// <summary>Đọc được serial — Export sẽ đổi tên thư mục và file theo serial này.</summary>
    Read,

    /// <summary>Mở được PDF nhưng không tìm ra chuỗi khớp mẫu serial — giữ nguyên tên, vào nhóm lỗi.</summary>
    NotFound,

    /// <summary>PDF hỏng / có mật khẩu / không có trang nào — giữ nguyên tên, vào nhóm lỗi.</summary>
    OpenError
}

/// <summary>Kết quả đọc serial của một file GCN nguồn.</summary>
public sealed class SerialScan
{
    public required string SourcePath { get; init; }

    /// <summary>Đường dẫn tương đối tính từ thư mục nguồn đã chọn.</summary>
    public required string RelativePath { get; init; }

    public SerialReadStatus Status { get; init; }

    /// <summary>Serial đã chuẩn hoá (VD "AA 123456"); rỗng khi không đọc được.</summary>
    public string Serial { get; init; } = "";

    /// <summary>Lý do lỗi / ghi chú, đưa vào error log để tra khi cần.</summary>
    public string Note { get; init; } = "";

    /// <summary>File cần người dùng xử lý tay — đây là tập mà nút "File lỗi" xuất ra.</summary>
    public bool NeedsReview => Status != SerialReadStatus.Read;
}

/// <summary>Đầu vào của bước dựng cây kết quả.</summary>
/// <param name="SourceRoot">Thư mục nguồn người dùng đã chọn.</param>
/// <param name="DestinationRoot">Thư mục người dùng chọn lúc Export (cây kết quả nằm TRONG thư mục con của nó).</param>
/// <param name="Scans">Kết quả đọc serial của MỌI file GCN đã quét, kể cả file không đọc được.</param>
public sealed record SerialRenameExportRequest(
    string SourceRoot,
    string DestinationRoot,
    IReadOnlyList<SerialScan> Scans);

/// <summary>Thống kê sau khi dựng xong cây kết quả.</summary>
public sealed class SerialRenameExportResult
{
    /// <summary>Số thư mục nhãn đã tạo (mỗi GCN đọc được serial một thư mục).</summary>
    public int LabelFolders { get; set; }

    /// <summary>Số file GCN đã đổi tên thành <c>{serial}-GCN.pdf</c>.</summary>
    public int GcnRenamed { get; set; }

    /// <summary>Số file copy nguyên tên (file kèm theo + GCN không đọc được serial).</summary>
    public int CopiedAsIs { get; set; }

    /// <summary>Số thư mục giữ nguyên tên vì bên trong không có GCN nào đọc được serial.</summary>
    public int FoldersKeptOriginalName { get; set; }

    /// <summary>Gốc cây kết quả đã ghi ra (đã tính cả hậu tố chống trùng).</summary>
    public string RootPath { get; set; } = "";

    public List<string> Warnings { get; } = new();
}
