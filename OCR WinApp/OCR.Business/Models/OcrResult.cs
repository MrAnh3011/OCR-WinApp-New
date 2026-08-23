namespace OCR.Business.Models;

/// <summary>Khung bao quanh một dòng/từ (tọa độ theo pixel của ảnh trang).</summary>
public sealed record BoundingBox(double X, double Y, double Width, double Height);

/// <summary>Một dòng text nhận dạng được.</summary>
public sealed class OcrLine
{
    public string Text { get; init; } = string.Empty;

    /// <summary>Độ tin cậy 0..1. Engine local (Windows.Media.Ocr) không trả về nên để 0.</summary>
    public double Confidence { get; init; }

    public BoundingBox? BoundingBox { get; init; }
}

/// <summary>Kết quả OCR của một trang.</summary>
public sealed class OcrPage
{
    public int PageNumber { get; init; }
    public string FullText { get; init; } = string.Empty;
    public IReadOnlyList<OcrLine> Lines { get; init; } = new List<OcrLine>();
}

/// <summary>Kết quả OCR của một file (có thể nhiều trang).</summary>
public sealed class OcrResult
{
    public string FileName { get; init; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; init; }
    public int PageCount => Pages.Count;
    public string FullText { get; init; } = string.Empty;
    public IReadOnlyList<OcrPage> Pages { get; init; } = new List<OcrPage>();
}
