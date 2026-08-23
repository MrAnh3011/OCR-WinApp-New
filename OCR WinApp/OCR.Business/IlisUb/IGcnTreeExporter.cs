using System.Collections.Generic;
using System.Threading;

namespace OCR.Business.IlisUb;

/// <summary>Một file GCN đã OCR THÀNH CÔNG kèm số serial đọc được (có thể rỗng).</summary>
public sealed record GcnTreeSource(string SourcePath, string? Serial);

/// <summary>Yêu cầu dựng cây thư mục đích khi Export màn OCR GCN iLis-UB.</summary>
/// <param name="SourceRoot">Thư mục cha người dùng đã chọn lúc quét.</param>
/// <param name="DestinationRoot">Thư mục người dùng chọn lúc Export.</param>
/// <param name="Sources">Các file GCN đã OCR THÀNH CÔNG (mỗi cái sinh một thư mục nhãn).</param>
/// <param name="Rules">Bộ quy tắc nạp LẠI tại thời điểm Export.</param>
/// <param name="ScannedGcnPaths">
/// TOÀN BỘ đường dẫn file đã được bộ lọc đầu vào nhận lúc quét — gồm cả file OCR lỗi, không chỉ file
/// thành công. Bộ lọc đầu vào chạy với <c>GcnKeyword</c> lúc quét, còn <paramref name="Rules"/> được nạp
/// lại lúc Export; người dùng sửa <c>GcnKeyword</c> giữa hai thời điểm thì file GCN sẽ không còn khớp
/// rule và bị coi là "file kèm theo" (gộp vào GT/GTK) — danh sách này loại chúng theo ĐƯỜNG DẪN nên
/// không phụ thuộc rule.
/// </param>
public sealed record GcnTreeExportRequest(
    string SourceRoot,
    string DestinationRoot,
    IReadOnlyList<GcnTreeSource> Sources,
    GcnFolderRules Rules,
    IReadOnlyCollection<string> ScannedGcnPaths);

/// <summary>Thống kê để hiện lên StatusMessage sau khi Export.</summary>
public sealed record GcnTreeExportResult(
    string RootPath,
    int HoSoFolders,
    int LabelFolders,
    int GcnFilesCopied,
    int MergedFiles,
    int CopiedAsIsFiles,
    int MissingSerialFolders,
    IReadOnlyList<string> Warnings);

/// <summary>Dựng cây thư mục đích: đổi tên theo serial, gộp/copy file kèm theo.</summary>
public interface IGcnTreeExporter
{
    GcnTreeExportResult Export(GcnTreeExportRequest request, CancellationToken ct = default);
}
