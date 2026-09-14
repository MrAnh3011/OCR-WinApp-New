using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using OCR.Business.Models;
using OCR.Business.VietBdGcn;

namespace OCR.Business.VbdBn;

/// <summary>
/// Exporter màn VBD-BN: (1) uỷ quyền cho exporter VietBD ghi sheet KeKhaiDangKy y nguyên luồng VBD;
/// (2) mở lại file vừa ghi, điền sheet ThongTinCCCD từ dòng 5 theo 16 cột A→P của template
/// Excel_Template_VietBD_BN. KHÔNG sửa exporter VietBD.
/// </summary>
public sealed class VbdBnExcelExporter : IVbdBnExcelExporter
{
    private const string CccdSheetName = "ThongTinCCCD";
    private static readonly XLColor WarnColor = XLColor.FromArgb(0xFF, 0xF4, 0xCC, 0xCC);

    private readonly IVietBdGcnExcelExporter _gcnExporter;

    public VbdBnExcelExporter(IVietBdGcnExcelExporter gcnExporter) => _gcnExporter = gcnExporter;

    public (int GcnRows, int CccdRows) Write(
        IEnumerable<VietBdGcnEnvelope> gcnEnvelopes,
        IReadOnlyList<CccdExportRow> cccdRows,
        string outputPath,
        string templatePath,
        string? maXa)
    {
        int gcnRowCount = _gcnExporter.Write(gcnEnvelopes, outputPath, templatePath, maXa);

        using var wb = new XLWorkbook(outputPath);
        var ws = wb.Worksheets.FirstOrDefault(s => s.Name == CccdSheetName)
                 ?? throw new InvalidOperationException(
                     $"Template không có sheet {CccdSheetName} — dùng đúng Excel_Template_VietBD_BN.xlsx.");

        // Dọn dữ liệu cũ từ dòng 5 (template sạch thì không có gì, nhưng Write phải idempotent).
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 4;
        for (int r = 5; r <= lastRow; r++) ws.Row(r).Clear();

        int row = 5;
        foreach (var item in cccdRows)
        {
            var g = item.GiayTo;
            ws.Cell(row, 1).Value = item.SoSerialGcn;
            ws.Cell(row, 2).Value = DisplayLoai(g.loai_giay_to);
            ws.Cell(row, 3).Value = g.so_giay_to ?? "";
            ws.Cell(row, 4).Value = g.ho_ten ?? "";
            ws.Cell(row, 5).Value = g.ngay_sinh ?? "";
            ws.Cell(row, 6).Value = g.gioi_tinh ?? "";
            ws.Cell(row, 7).Value = g.quoc_tich ?? "";
            ws.Cell(row, 8).Value = g.que_quan ?? "";
            ws.Cell(row, 9).Value = g.noi_thuong_tru ?? "";
            ws.Cell(row, 10).Value = g.ngay_cap ?? "";
            ws.Cell(row, 11).Value = g.noi_cap ?? "";
            ws.Cell(row, 12).Value = g.co_gia_tri_den ?? "";
            ws.Cell(row, 13).Value = item.FileNguon;
            ws.Cell(row, 14).Value = g.trang is { Count: > 0 } ? string.Join(", ", g.trang) : "";
            ws.Cell(row, 15).Value = g.do_tin_cay ?? "";

            var warnings = (g.canh_bao ?? new List<string>()).Concat(item.CanhBaoBoSung).ToList();
            ws.Cell(row, 16).Value = string.Join("\n", warnings);
            ws.Cell(row, 16).Style.Alignment.WrapText = true;
            if (warnings.Count > 0)
                ws.Range(row, 1, row, 16).Style.Fill.SetBackgroundColor(WarnColor);
            row++;
        }

        // Workaround ClosedXML 0.104.2 (giống các exporter khác): tắt AutoFilter trước khi lưu.
        foreach (var sheet in wb.Worksheets)
            if (sheet.AutoFilter is not null && sheet.AutoFilter.IsEnabled)
                sheet.AutoFilter.IsEnabled = false;

        wb.Save();
        return (gcnRowCount, cccdRows.Count);
    }

    private static string DisplayLoai(string? loai) => loai?.Trim().ToLowerInvariant() switch
    {
        "cmnd" => "CMND",
        "cccd" => "CCCD",
        "can_cuoc" => "Căn cước",
        null or "" => "",
        var other => other
    };
}
