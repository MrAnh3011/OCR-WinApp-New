using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;

namespace OCR.Business.UyBan;

/// <summary>Render trang 1 PDF → ảnh → gọi API vision → trích xuất trường "Đất Uỷ Ban".</summary>
public interface IUyBanExtractService
{
    Task<UyBanRecord> ExtractAsync(
        string pdfPath,
        CancellationToken ct = default,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null);
}
