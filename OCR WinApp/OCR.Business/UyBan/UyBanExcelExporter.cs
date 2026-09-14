using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using OCR.Business.Models;

namespace OCR.Business.UyBan;

/// <summary>
/// Ghi UyBanRecord ra Excel (sheet "Data", từ dòng 5) theo mapping cột của excel_writer.py.
/// </summary>
public sealed class UyBanExcelExporter : IUyBanExcelExporter
{
    private const string SheetName = "Data";

    public void Write(IEnumerable<UyBanRecord> records, string outputPath, string templatePath)
    {
        var resolved = ResolveTemplate(templatePath);
        bool templateExists = File.Exists(resolved);

        using var wb = templateExists ? new XLWorkbook(resolved) : new XLWorkbook();
        var ws = wb.Worksheets.FirstOrDefault(s => s.Name == SheetName)
                 ?? wb.Worksheets.FirstOrDefault()
                 ?? wb.Worksheets.Add(SheetName);

        // Xóa dữ liệu cũ từ dòng 5 (giữ header 1-4).
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 4;
        if (lastRow >= 5) ws.Rows(5, lastRow).Delete();

        int row = 5;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in records)
        {
            // Bỏ trùng theo (Họ tên, Thửa, Tờ).
            var key = $"{rec.HoVaTen.Trim()}|{rec.ThuaDatSo.Trim()}|{rec.ToBanDoSo.Trim()}";
            if (key != "||" && !seen.Add(key)) continue;

            WriteRow(ws, row, rec);
            row++;
        }

        // ClosedXML 0.104.2 ném NotSupportedException ở PopulateAutoFilter khi template có sẵn AutoFilter
        // → tắt AutoFilter trên mọi sheet trước khi lưu (workaround).
        foreach (var sheet in wb.Worksheets)
            if (sheet.AutoFilter is not null && sheet.AutoFilter.IsEnabled)
                sheet.AutoFilter.IsEnabled = false;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        wb.SaveAs(outputPath);
    }

    private static void WriteRow(IXLWorksheet ws, int row, UyBanRecord rec)
    {
        var toBd = CleanAddress(rec.ToBanDoSo);
        var thua = CleanAddress(rec.ThuaDatSo);

        Set(ws, "B", row, (!string.IsNullOrEmpty(toBd) && !string.IsNullOrEmpty(thua)) ? $"{toBd}_{thua}" : "");
        Set(ws, "F", row, "GDC");
        Set(ws, "I", row, rec.HoVaTen);

        // DIA_CHI_4 → S/T/U (3/2/1 phần) + V (đầy đủ, đã sạch dấu chấm).
        var cleaned4 = CleanAddress(rec.DiaChi4);
        var parts = cleaned4.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        string s = "", t = "", u = "";
        if (parts.Count == 3) { s = parts[0]; t = parts[1]; u = parts[2]; }
        else if (parts.Count == 2) { t = parts[0]; u = parts[1]; }
        else if (parts.Count == 1) { u = parts[0]; }
        else if (parts.Count > 3) { t = string.Join(", ", parts.Take(parts.Count - 1)); u = parts[^1]; }
        Set(ws, "S", row, s);
        Set(ws, "T", row, t);
        Set(ws, "U", row, u);
        Set(ws, "V", row, cleaned4);

        Set(ws, "AQ", row, rec.ThuaDatSo);
        Set(ws, "AR", row, rec.ToBanDoSo);

        var dienTich = CleanNumeric(rec.DienTich6);
        Set(ws, "AZ", row, dienTich);
        Set(ws, "BA", row, dienTich);

        Set(ws, "BB", row, rec.MucDich7);
        Set(ws, "BC", row, rec.MucDich7);

        var chung = CleanNumeric(rec.SuDungChung);
        var rieng = CleanNumeric(rec.SuDungRieng);
        if (!string.IsNullOrEmpty(chung)) { Set(ws, "BD", row, chung); Set(ws, "BE", row, 1); }
        else if (!string.IsNullOrEmpty(rieng)) { Set(ws, "BD", row, rieng); Set(ws, "BE", row, 0); }

        Set(ws, "BF", row, rec.ThoiHan8);

        var pdfName = Path.GetFileNameWithoutExtension(rec.FileName) + ".pdf";
        Set(ws, "FW", row, pdfName);
    }

    private static void Set(IXLWorksheet ws, string col, int row, object? value)
    {
        if (value is null) return;
        if (value is string str && string.IsNullOrEmpty(str)) return;
        ws.Cell($"{col}{row}").Value = XLCellValue.FromObject(value);
    }

    private static string CleanAddress(string? s)
        => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\.", "").Trim();

    private static string CleanNumeric(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        var m = Regex.Match(val.Trim(), @"([0-9]+(?:[.,][0-9]+)*)");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string ResolveTemplate(string templatePath)
    {
        if (string.IsNullOrEmpty(templatePath)) return templatePath;
        if (File.Exists(templatePath)) return templatePath;

        // Đường dẫn tương đối thư mục exe (vd: Assets\Temp\Excel_FormMau_v5.xlsx).
        var rel = Path.Combine(AppContext.BaseDirectory, templatePath);
        if (File.Exists(rel)) return rel;

        var fileName = Path.GetFileName(templatePath);
        var local = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(local)) return local;

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var test = Path.Combine(dir, fileName);
            if (File.Exists(test)) return test;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return templatePath;
    }
}
