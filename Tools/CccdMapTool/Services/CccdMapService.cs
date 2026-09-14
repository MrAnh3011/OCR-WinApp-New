using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CccdMapTool.Models;

namespace CccdMapTool.Services;

/// <summary>
/// Map thông tin từ sheet <c>ThongTinCCCD</c> sang sheet <c>KeKhaiDangKy</c> trong CÙNG một file Excel
/// (file kết quả màn OCR GCN VBD-BN — chỉ template Excel_Template_VietBD_BN.xlsx mới có đủ hai sheet này).
///
/// Quy tắc đã chốt với chủ dự án 09/09/2026:
///   - Chỉ khớp trong phạm vi CÙNG số serial GCN (ThongTinCCCD cột A ↔ KeKhaiDangKy cột C, dự phòng cột N).
///     Ưu tiên khớp số giấy tờ (so sánh chỉ chữ số), không khớp được mới khớp họ tên (bỏ dấu, bỏ tiền tố
///     "Ông/Bà/Hộ ông/Hộ bà"). Nhiều bản ghi cùng khớp → KHÔNG map, ghi cảnh báo.
///   - CHỈ điền vào ô đang TRỐNG. Ô đã có sẵn mà giá trị lệch → giữ nguyên giá trị trên GCN + cảnh báo
///     nêu cả hai giá trị (dữ liệu đọc từ chính GCN có giá trị pháp lý cao hơn).
///   - Cảnh báo ghi vào cột MỚI <c>FI</c>, ngay sau ba cột phụ sẵn có FF/FG/FH của exporter VietBD.
///
/// ⚠️ Một GCN chiếm NHIỀU dòng trong KeKhaiDangKy (mỗi mục đích sử dụng / mỗi chủ một dòng) nên cùng một
/// người lặp lại ở nhiều dòng. Vì vậy một bản ghi CCCD KHÔNG bị "tiêu thụ" sau lần khớp đầu — nếu đánh dấu
/// đã dùng thì các dòng sau của chính người đó sẽ không được điền.
/// </summary>
public sealed class CccdMapService
{
    public const string SheetKeKhai = "KeKhaiDangKy";
    public const string SheetCccd = "ThongTinCCCD";

    private const int FirstDataRow = 5;

    /// <summary>Cột cảnh báo do tool này thêm — nằm sau FE (tên file), FF, FG, FH của exporter VietBD.</summary>
    private const string ColCanhBaoMap = "FI";

    private const string ColSerial = "C";
    private const string ColSerialDuPhong = "N";

    private static readonly XLColor WarnColor = XLColor.FromArgb(0xFF, 0xF4, 0xCC, 0xCC);

    private static readonly OwnerBlock ChuBlock = new(
        "Chủ sử dụng", ColHoTen: "AB", ColSoGiayTo: "AS", ColLoaiGiayTo: "AR", ColNgaySinh: "AC",
        ColGioiTinh: "AE", ColQuocTich: "AH", ColDiaChi: "AJ", ColNgayCap: "AT", ColNoiCap: "AU");

    private static readonly OwnerBlock VoChongBlock = new(
        "Vợ/chồng", ColHoTen: "AV", ColSoGiayTo: "BM", ColLoaiGiayTo: "BL", ColNgaySinh: "AW",
        ColGioiTinh: "AY", ColQuocTich: "BB", ColDiaChi: "BD", ColNgayCap: "BN", ColNoiCap: "BO");

    public MapResult Run(string inputPath, string outputPath, Action<string> log, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Không thấy file đầu vào: {inputPath}");
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("File kết quả phải khác file đầu vào — tool không ghi đè file gốc.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.Copy(inputPath, outputPath, overwrite: true);
        log($"Đã tạo bản sao để ghi kết quả: {Path.GetFileName(outputPath)}");

        try
        {
            return MapInto(outputPath, log, ct);
        }
        catch
        {
            // Hỏng giữa chừng (thiếu sheet, file khoá…) thì xoá bản sao dở để không để lại file rác.
            TryDelete(outputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { /* đang bị khoá — bỏ qua */ }
    }

    private static MapResult MapInto(string outputPath, Action<string> log, CancellationToken ct)
    {
        using var wb = new XLWorkbook(outputPath);
        var wsKeKhai = FindSheet(wb, SheetKeKhai);
        var wsCccd = FindSheet(wb, SheetCccd);

        var cccdRows = ReadCccd(wsCccd);
        log($"Đọc sheet {SheetCccd}: {cccdRows.Count} bản ghi giấy tờ.");
        if (cccdRows.Count == 0)
            log("⚠ Sheet ThongTinCCCD không có dữ liệu — mọi dòng sẽ bị ghi cảnh báo thiếu CCCD.");

        var bySerial = cccdRows
            .GroupBy(c => c.Serial, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        WriteWarningHeader(wsKeKhai);

        var used = new HashSet<int>();
        int rowsScanned = 0, blocksMatched = 0, fieldsFilled = 0, rowsWarned = 0;
        int lastRow = wsKeKhai.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            ct.ThrowIfCancellationRequested();

            string serial = NormSpace(Text(wsKeKhai, row, ColSerial));
            if (serial.Length == 0) serial = NormSpace(Text(wsKeKhai, row, ColSerialDuPhong));

            string tenChu = Text(wsKeKhai, row, ChuBlock.ColHoTen);
            string tenVo = Text(wsKeKhai, row, VoChongBlock.ColHoTen);
            string soGtChu = Text(wsKeKhai, row, ChuBlock.ColSoGiayTo);
            string soGtVo = Text(wsKeKhai, row, VoChongBlock.ColSoGiayTo);

            // Dòng trống hoàn toàn (đuôi bảng đã định dạng sẵn) → bỏ qua, không đếm, không cảnh báo.
            if (serial.Length == 0 && tenChu.Length == 0 && tenVo.Length == 0) continue;

            rowsScanned++;
            var warnings = new List<string>();
            var pool = bySerial.TryGetValue(serial, out var group) ? group : new List<CccdRecord>();

            if (serial.Length == 0)
            {
                warnings.Add("Dòng không có số serial GCN nên không tra được sheet ThongTinCCCD.");
            }
            else if (pool.Count == 0)
            {
                warnings.Add($"Không có bản ghi nào trong ThongTinCCCD mang serial \"{serial}\".");
            }
            else
            {
                ApplyBlock(wsKeKhai, row, ChuBlock, pool, tenChu, soGtChu, warnings, ref blocksMatched, ref fieldsFilled, used);

                // Khối vợ/chồng chỉ tồn tại khi dòng thực sự có người thứ hai — dòng cá nhân bỏ qua im lặng.
                if (tenVo.Length > 0 || soGtVo.Length > 0)
                    ApplyBlock(wsKeKhai, row, VoChongBlock, pool, tenVo, soGtVo, warnings, ref blocksMatched, ref fieldsFilled, used);
            }

            if (warnings.Count > 0)
            {
                var cell = wsKeKhai.Cell(row, ColCanhBaoMap);
                cell.Value = string.Join("\n", warnings.Distinct());
                cell.Style.Alignment.WrapText = true;
                cell.Style.Fill.SetBackgroundColor(WarnColor);
                rowsWarned++;
            }
        }

        // Workaround ClosedXML 0.104.2 (giống mọi exporter trong dự án): tắt AutoFilter trước khi lưu.
        foreach (var sheet in wb.Worksheets)
            if (sheet.AutoFilter is not null && sheet.AutoFilter.IsEnabled)
                sheet.AutoFilter.IsEnabled = false;

        wb.Save();

        int unused = cccdRows.Count - used.Count;
        return new MapResult(outputPath, rowsScanned, blocksMatched, fieldsFilled, rowsWarned, cccdRows.Count, unused);
    }

    private static void ApplyBlock(
        IXLWorksheet ws, int row, OwnerBlock block, List<CccdRecord> pool,
        string hoTen, string soGiayTo, List<string> warnings,
        ref int blocksMatched, ref int fieldsFilled, HashSet<int> used)
    {
        var outcome = Resolve(pool, soGiayTo, hoTen);
        if (outcome.Ambiguous)
        {
            warnings.Add($"{block.Name}: {outcome.Reason} — không map để tránh gán nhầm, cần đối chiếu thủ công.");
            return;
        }
        if (outcome.Record is null)
        {
            string mo = hoTen.Length > 0 ? $"\"{hoTen}\"" : "(không có tên)";
            warnings.Add($"{block.Name} {mo}: không tìm thấy bản ghi CCCD khớp trong ThongTinCCCD.");
            return;
        }

        var c = outcome.Record;
        used.Add(c.Row);
        blocksMatched++;

        int filled = 0;
        void Put(string col, string value, string fieldName)
        {
            if (value.Length == 0) return;
            var cell = ws.Cell(row, col);
            string current = cell.GetFormattedString().Trim();
            if (current.Length == 0)
            {
                cell.Value = value;
                filled++;
                return;
            }
            if (!SameValue(current, value))
                warnings.Add($"{block.Name} — {fieldName}: giữ nguyên \"{current}\" của GCN, CCCD ghi \"{value}\".");
        }

        Put(block.ColLoaiGiayTo, c.LoaiGiayTo, "loại giấy tờ");
        Put(block.ColSoGiayTo, c.SoGiayTo, "số giấy tờ");
        Put(block.ColNgaySinh, c.NgaySinh, "ngày sinh");
        Put(block.ColGioiTinh, c.GioiTinh, "giới tính");
        Put(block.ColQuocTich, c.QuocTich, "quốc tịch");
        Put(block.ColNgayCap, c.NgayCap, "ngày cấp giấy tờ");
        Put(block.ColNoiCap, c.NoiCap, "nơi cấp giấy tờ");

        // Địa chỉ trên GCN và nơi thường trú trên CCCD là hai thứ khác nhau về pháp lý: chỉ điền khi trống,
        // và luôn ghi rõ nguồn để người nhập VBDLIS biết dòng này lấy địa chỉ từ CCCD.
        if (c.NoiThuongTru.Length > 0 && Text(ws, row, block.ColDiaChi).Length == 0)
        {
            ws.Cell(row, block.ColDiaChi).Value = c.NoiThuongTru;
            filled++;
            warnings.Add($"{block.Name} — địa chỉ: lấy \"Nơi thường trú\" trên CCCD do GCN bỏ trống, cần đối chiếu.");
        }

        fieldsFilled += filled;
    }

    /// <summary>Tra một khối chủ trong nhóm CCCD cùng serial: số giấy tờ trước, họ tên sau.</summary>
    private static MatchOutcome Resolve(List<CccdRecord> pool, string soGiayTo, string hoTen)
    {
        string digits = Digits(soGiayTo);
        if (digits.Length >= 6)
        {
            var bySo = pool.Where(c => Digits(c.SoGiayTo) == digits).ToList();
            if (bySo.Count == 1) return MatchOutcome.Found(bySo[0], "số giấy tờ");
            if (bySo.Count > 1) return MatchOutcome.AmbiguousBy($"có {bySo.Count} bản ghi CCCD cùng số giấy tờ \"{soGiayTo}\"");
        }

        string name = NormName(hoTen);
        if (name.Length > 0)
        {
            var byName = pool.Where(c => NormName(c.HoTen) == name).ToList();
            if (byName.Count == 1) return MatchOutcome.Found(byName[0], "họ tên");
            if (byName.Count > 1) return MatchOutcome.AmbiguousBy($"có {byName.Count} bản ghi CCCD cùng họ tên \"{hoTen}\"");
        }

        return MatchOutcome.NotFound();
    }

    private static List<CccdRecord> ReadCccd(IXLWorksheet ws)
    {
        var list = new List<CccdRecord>();
        int last = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        for (int r = FirstDataRow; r <= last; r++)
        {
            var rec = new CccdRecord(
                Row: r,
                Serial: NormSpace(Text(ws, r, "A")),
                LoaiGiayTo: Text(ws, r, "B"),
                SoGiayTo: Text(ws, r, "C"),
                HoTen: Text(ws, r, "D"),
                NgaySinh: Text(ws, r, "E"),
                GioiTinh: Text(ws, r, "F"),
                QuocTich: Text(ws, r, "G"),
                NoiThuongTru: Text(ws, r, "I"),
                NgayCap: Text(ws, r, "J"),
                NoiCap: Text(ws, r, "K"));

            if (rec.SoGiayTo.Length == 0 && rec.HoTen.Length == 0) continue;
            list.Add(rec);
        }
        return list;
    }

    /// <summary>Tiêu đề cột cảnh báo, đặt đúng bố cục header của template (dòng 1 mã trường, dòng 3 tên Việt).</summary>
    private static void WriteWarningHeader(IXLWorksheet ws)
    {
        ws.Cell(1, ColCanhBaoMap).Value = "EXTRA_canhBaoMapCccd";
        ws.Cell(3, ColCanhBaoMap).Value = "Cảnh báo map CCCD";
    }

    private static IXLWorksheet FindSheet(XLWorkbook wb, string name)
        => wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException(
               $"File không có sheet \"{name}\" — cần file kết quả của màn OCR GCN VBD-BN " +
               $"(template Excel_Template_VietBD_BN.xlsx, có đủ hai sheet {SheetKeKhai} và {SheetCccd}).");

    private static string Text(IXLWorksheet ws, int row, string col)
        => ws.Cell(row, col).GetFormattedString().Trim();

    private static string NormSpace(string v) => Regex.Replace(v.Trim(), @"\s+", " ");

    private static string Digits(string v) => new(v.Where(char.IsDigit).ToArray());

    /// <summary>So sánh "đã có sẵn" vs "CCCD": bỏ qua khác biệt hoa/thường, khoảng trắng và cách viết ngày.</summary>
    private static bool SameValue(string a, string b)
    {
        string x = NormSpace(a), y = NormSpace(b);
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true;
        return NormDate(x) is { Length: > 0 } dx && NormDate(y) is { Length: > 0 } dy && dx == dy;
    }

    /// <summary>Đưa chuỗi ngày về dd/MM/yyyy để so sánh; không phải ngày thì trả chuỗi rỗng.</summary>
    private static string NormDate(string v)
    {
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd"];
        return DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>Chuẩn hoá họ tên để khớp: bỏ tiền tố xưng hô, bỏ dấu, gộp khoảng trắng, về chữ thường.</summary>
    private static string NormName(string hoTen)
    {
        string s = NormSpace(hoTen).ToLowerInvariant();
        s = Regex.Replace(s, @"^(hộ\s*ông|hộ\s*bà|hộ|ông|bà|anh|chị)\s*[:.]?\s*", "");
        s = Regex.Replace(s, @"\(.*?\)", " ");           // bỏ phần ghi chú trong ngoặc đơn
        return NormSpace(BoDau(s));
    }

    /// <summary>Bỏ dấu tiếng Việt (NFD + bỏ dấu phụ; đ/Đ xử lý tay vì NFD không tách được).</summary>
    private static string BoDau(string v)
    {
        var formD = v.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
