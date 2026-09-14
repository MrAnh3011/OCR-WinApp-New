using System.Collections.Generic;
using OCR.Business.Models;

namespace OCR.Business.VbdBn;

/// <summary>Một dòng của sheet ThongTinCCCD: một giấy tờ tuỳ thân đọc từ file GTK, đã ghép với serial GCN cùng thư mục.</summary>
public sealed record CccdExportRow(
    string SoSerialGcn,                       // đã nối "; " sẵn từ VbdBnSerialMap.Resolve
    CccdRecord GiayTo,
    string FileNguon,                         // tên file GTK (không kèm đường dẫn)
    IReadOnlyList<string> CanhBaoBoSung);     // cảnh báo từ serial-map, nối thêm vào canh_bao của record

/// <summary>Ghi hai sheet của khuôn Excel_Template_VietBD_BN: KeKhaiDangKy (uỷ quyền cho <see cref="OCR.Business.VietBdGcn.IVietBdGcnExcelExporter"/>) và ThongTinCCCD.</summary>
public interface IVbdBnExcelExporter
{
    /// <returns>(số dòng sheet KeKhaiDangKy, số dòng sheet ThongTinCCCD)</returns>
    (int GcnRows, int CccdRows) Write(
        IEnumerable<VietBdGcnEnvelope> gcnEnvelopes,
        IReadOnlyList<CccdExportRow> cccdRows,
        string outputPath,
        string templatePath,
        string? maXa);
}
