namespace OCR.Business.Pdf;

/// <summary>Chuan hoa huong PDF bang cach chi cap nhat metadata Rotate, khong rasterize lai noi dung trang.</summary>
public interface IPdfRotationNormalizer
{
    /// <summary>Thu OCR 4 huong va xoay tung trang ve huong doc chuan. Tra ve so trang da cap nhat rotate.</summary>
    Task<int> NormalizeAsync(string pdfPath, CancellationToken ct = default);
}
