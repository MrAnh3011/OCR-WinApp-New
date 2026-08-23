using OCR.Business.Models;
using Windows.Graphics.Imaging;

namespace OCR.Business.Ocr;

/// <summary>
/// Nhận dạng text từ một ảnh (một trang). Trừu tượng hóa để đổi engine
/// local (Windows.Media.Ocr) ↔ cloud (Azure) mà không ảnh hưởng tầng trên.
/// </summary>
public interface IOcrEngine
{
    Task<OcrPage> RecognizeAsync(SoftwareBitmap bitmap, int pageNumber, string language, CancellationToken ct = default);
}
