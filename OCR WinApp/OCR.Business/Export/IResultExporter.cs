using OCR.Business.Models;

namespace OCR.Business.Export;

/// <summary>Ghi kết quả OCR ra một định dạng file. Mỗi định dạng = một implementation.</summary>
public interface IResultExporter
{
    /// <summary>Phần mở rộng file đầu ra, ví dụ ".json", ".xlsx".</summary>
    string Extension { get; }

    Task ExportAsync(OcrResult result, string outputPath, CancellationToken ct = default);
}
