using OCR.Business.Models;

namespace OCR.Business.Processors;

/// <summary>
/// Một loại tài liệu OCR. Mỗi processor là một mục trên menu side panel.
/// Thêm loại mới = thêm một class implement interface này + đăng ký DI.
/// </summary>
public interface IDocumentProcessor
{
    DocumentFeature Feature { get; }

    /// <summary>Quét toàn bộ file trong <paramref name="inputFolder"/>, OCR và ghi kết quả ra <paramref name="outputFolder"/>.</summary>
    Task<ProcessReport> ProcessAsync(
        string inputFolder,
        string outputFolder,
        ProcessOptions options,
        IProgress<ProcessProgress>? progress,
        CancellationToken ct = default);

    /// <summary>OCR đúng danh sách file đã chọn (kéo-thả/duyệt thư mục), ghi kết quả ra <paramref name="outputFolder"/>.</summary>
    Task<ProcessReport> ProcessFilesAsync(
        IReadOnlyList<string> files,
        string outputFolder,
        ProcessOptions options,
        IProgress<ProcessProgress>? progress,
        CancellationToken ct = default);

    /// <summary>Phần mở rộng file được hỗ trợ (đã gồm dấu chấm, chữ thường), ví dụ ".pdf".</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }
}
