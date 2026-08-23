using Windows.Graphics.Imaging;

namespace OCR.Business.Pdf;

/// <summary>Render từng trang của một file PDF ra ảnh để đưa vào OCR.</summary>
public interface IPdfRenderer
{
    Task<IReadOnlyList<SoftwareBitmap>> RenderPagesAsync(string pdfPath, CancellationToken ct = default);

    /// <summary>Render đúng MỘT trang PDF ra ảnh JPEG (byte[]) — dùng cho pipeline gửi ảnh lên API vision.</summary>
    Task<byte[]> RenderPageJpegAsync(string pdfPath, int pageIndex = 0, CancellationToken ct = default);

    /// <summary>Render TẤT CẢ trang PDF ra ảnh JPEG (mỗi trang 1 byte[]) — dùng khi gửi nhiều trang lên API vision.</summary>
    Task<IReadOnlyList<byte[]>> RenderPagesJpegAsync(string pdfPath, CancellationToken ct = default);
}
