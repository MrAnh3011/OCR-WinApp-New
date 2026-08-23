namespace OCR.Business.BlankPage;

/// <summary>Dựng cây thư mục kết quả (PDF đã bỏ trang trắng + file khác copy nguyên) và ghi báo cáo CSV.</summary>
public interface IBlankPageTreeExporter
{
    BlankPageExportResult Export(BlankPageExportRequest request, CancellationToken ct = default);
}
