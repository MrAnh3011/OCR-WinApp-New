namespace OCR.Business.SerialRename;

/// <summary>Đọc số serial từ trang 1 của một file GCN. KHÔNG sửa/ghi gì trên đĩa.</summary>
public interface ISerialReader
{
    Task<SerialScan> ReadAsync(string pdfPath, string relativePath, CancellationToken ct = default);
}
