using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;

namespace OCR.Business.Split;

public enum SplitGcnVariant
{
    Standard,
    New,
    NoGcn
}

/// <summary>Kết quả tách 1 file PDF.</summary>
public sealed class SplitGcnResult
{
    public int GcnCount { get; set; }
    public int FilesCreated { get; set; }
    /// <summary>Số output đã tạo đủ toàn bộ file bắt buộc của biến thể.</summary>
    public int CompleteSetCount { get; set; }
    public int PagesRotated { get; set; }
    public IReadOnlyList<string> MissingFileFolders { get; set; } = Array.Empty<string>();
}

/// <summary>Tách 1 file PDF gốc thành nhiều bộ GCN/GT/GTK, ghi ra thư mục tạm.</summary>
public interface ISplitGcnService
{
    /// <param name="pdfPath">File PDF gốc.</param>
    /// <param name="allocator">Bộ cấp phát tên thư mục con duy nhất (dùng chung cho cả phiên).</param>
    /// <param name="normalizePageRotation">True nếu cần tự phát hiện và xoay từng trang PDF con về cùng hướng đọc sau khi cắt.</param>
    Task<SplitGcnResult> SplitAsync(
        string pdfPath,
        LabelAllocator allocator,
        bool normalizePageRotation = false,
        SplitGcnVariant variant = SplitGcnVariant.Standard,
        string? jsonPath = null,
        bool useCachedJson = false,
        CancellationToken ct = default,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null);
}
