using System.Collections.Generic;
using OCR.Business.Models;

namespace OCR.Business.DocxVbd;

/// <summary>
/// Convert một file .docx "Sổ cấp GCN" thành danh sách <see cref="VietBdGcnEnvelope"/> (mỗi trang sổ
/// = một envelope) — THUẦN CODE, không gọi API OCR, không trừ hạn mức.
/// </summary>
public interface IDocxVbdConvertService
{
    /// <summary>
    /// Đọc + map toàn bộ trang sổ trong một file docx. File không có trang sổ nào (sai mẫu) trả danh
    /// sách rỗng — nơi gọi tự quyết định coi đó là lỗi hay không.
    /// </summary>
    IReadOnlyList<VietBdGcnEnvelope> ConvertFile(string filePath);
}
