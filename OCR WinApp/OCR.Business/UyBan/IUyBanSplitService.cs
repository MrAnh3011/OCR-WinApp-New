using System.Collections.Generic;

namespace OCR.Business.UyBan;

/// <summary>
/// Tách 1 file PDF lớn (nhiều đơn của 1 địa phương) thành các file PDF con,
/// mỗi file 2 trang (đơn cuối có thể 1 trang nếu tổng số trang lẻ).
/// </summary>
public interface IUyBanSplitService
{
    /// <summary>Tách <paramref name="pdfPath"/> thành các PDF con 2 trang ghi vào <paramref name="outDir"/>; trả về danh sách đường dẫn PDF con.</summary>
    IReadOnlyList<string> SplitInto2Pages(string pdfPath, string outDir);
}
