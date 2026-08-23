namespace OCR.Business.BlankPage;

/// <summary>Phân tích một file PDF để tìm các trang trắng. KHÔNG ghi/sửa gì trên đĩa.</summary>
public interface IBlankPageScanner
{
    Task<BlankPageScan> ScanAsync(string pdfPath, string relativePath, CancellationToken ct = default);
}
