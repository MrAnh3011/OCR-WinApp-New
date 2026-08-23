namespace OCR.Business.SerialRename;

/// <summary>Dựng cây kết quả của màn "Đổi tên theo Serial". Không bao giờ ghi vào thư mục nguồn.</summary>
public interface ISerialRenameTreeExporter
{
    SerialRenameExportResult Export(SerialRenameExportRequest request, CancellationToken ct = default);
}
