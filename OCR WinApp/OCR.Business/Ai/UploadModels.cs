using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

/// <summary>Một tệp cần đẩy lên Gemini cho một việc. Một nguồn có thể sinh nhiều artifact (PDF nhiều trang → nhiều ảnh).</summary>
public sealed record UploadArtifact(string ArtifactKey, string DisplayName, string MimeType, byte[] Content);

/// <summary>
/// Việc đã sẵn sàng cho luồng inference.
/// <paramref name="Files"/> giữ ĐÚNG thứ tự delegate chuẩn bị artifact trả về — nhánh ảnh nhiều trang phụ thuộc vào thứ tự này.
/// <paramref name="SkippedUpload"/> = true khi bỏ qua upload (trúng cache JSON, hoặc provider không phải Gemini).
/// </summary>
public sealed record UploadWorkItem(string SourcePath, IReadOnlyList<GeminiFileReference> Files, bool SkippedUpload);

/// <summary>File không chuẩn bị/upload được sau khi đã thử hết lượt.</summary>
public sealed record UploadFailure(string SourcePath, string Reason);

/// <summary>Mô tả một phiên upload cho pipeline dùng chung.</summary>
public sealed class GeminiUploadRequest
{
    /// <summary>Danh sách nguồn cần xử lý. Với Đất Uỷ Ban đây là danh sách PDF con, không phải PDF gốc.</summary>
    public required IReadOnlyList<string> SourcePaths { get; init; }

    /// <summary>Dựng byte cần gửi cho một nguồn. Nơi đặt bước render/tách riêng của từng pipeline.</summary>
    public required Func<string, CancellationToken, Task<IReadOnlyList<UploadArtifact>>> PrepareArtifactsAsync { get; init; }

    /// <summary>
    /// Trả true nếu nguồn này chắc chắn trúng cache JSON và sẽ không gọi AI — khi đó bỏ qua upload.
    /// Truyền null nếu pipeline không có cache JSON (Đất Uỷ Ban).
    /// </summary>
    public Func<string, bool>? WillHitJsonCache { get; init; }

    /// <summary>
    /// Handler chốt sổ, được gắn vào hàng đợi TRƯỚC khi producer chạy.
    /// Bắt buộc dùng đường này thay vì tự `+=` sau khi gọi Start: producer chạy ngay trong Start,
    /// nguồn chốt sớm sẽ làm mất sự kiện và tiến độ không bao giờ đủ 100%.
    /// </summary>
    public Action<string, bool>? OnItemSettled { get; init; }

    /// <summary>
    /// Báo khi producer bắt đầu chuẩn bị và tải một nguồn lên. Gắn TRƯỚC khi producer chạy,
    /// cùng lý do với <see cref="OnItemSettled"/>: producer chạy ngay trong Start nên đăng ký
    /// sau sẽ bỏ lỡ những nguồn bắt đầu sớm.
    /// </summary>
    public Action<string>? OnItemUploading { get; init; }
}
