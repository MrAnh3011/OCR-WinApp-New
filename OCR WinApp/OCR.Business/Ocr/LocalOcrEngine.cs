using OCR.Business.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcr = Windows.Media.Ocr;

namespace OCR.Business.Ocr;

/// <summary>
/// Engine OCR mặc định cho MVP: dùng Windows.Media.Ocr (offline, miễn phí).
/// Cần gói ngôn ngữ tương ứng cài trong Windows (Settings → Time &amp; Language → Language).
/// </summary>
public sealed class LocalOcrEngine : IOcrEngine
{
    public async Task<OcrPage> RecognizeAsync(SoftwareBitmap bitmap, int pageNumber, string language, CancellationToken ct = default)
    {
        var engine = WinOcr.OcrEngine.TryCreateFromLanguage(new Language(language))
                     ?? WinOcr.OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException(
                         $"Không tạo được OCR engine cho ngôn ngữ '{language}'. " +
                         "Hãy cài gói ngôn ngữ trong Windows (Settings → Time & Language → Language).");

        // OcrEngine yêu cầu định dạng pixel cụ thể.
        var input = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? bitmap
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(input);

        var lines = result.Lines
            .Select(l => new OcrLine
            {
                Text = l.Text,
                Confidence = 0d, // Windows.Media.Ocr không cung cấp độ tin cậy theo dòng
                BoundingBox = ComputeBox(l)
            })
            .ToList();

        return new OcrPage
        {
            PageNumber = pageNumber,
            FullText = result.Text,
            Lines = lines
        };
    }

    private static BoundingBox? ComputeBox(WinOcr.OcrLine line)
    {
        if (line.Words.Count == 0) return null;

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var w in line.Words)
        {
            var r = w.BoundingRect;
            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.X + r.Width);
            bottom = Math.Max(bottom, r.Y + r.Height);
        }
        return new BoundingBox(left, top, right - left, bottom - top);
    }
}
