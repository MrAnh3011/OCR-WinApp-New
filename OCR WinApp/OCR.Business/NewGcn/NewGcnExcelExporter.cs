using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using OCR.Business.Models;

namespace OCR.Business.NewGcn;

/// <summary>
/// Ghi envelope GCN (New) ra Excel_FormMau_v3.xlsx (sheet "Data", từ dòng 5).
/// Port nguyên mapping cột từ GcnOcrApp.NewExtractService (SyncEnvelopeToExcel/WriteEnvelopeRow/WriteCoOwnerRowNew).
/// </summary>
public sealed class NewGcnExcelExporter : INewGcnExcelExporter
{
    private const string SheetName = "Data";

    // Giá trị hằng mặc định điền sẵn vào Excel (không trích xuất từ giấy).
    // KHÔNG điền sẵn dân tộc: GCN không in dân tộc nên mặc định "Kinh" chỉ là suy đoán, sai với
    // chủ sử dụng là người dân tộc thiểu số. Cột dân tộc (X / AO) để trống cho người dùng tự điền.
    private const string QuocTich = "Việt Nam";
    private const string LoaiBanDo = "Bản đồ địa chính (VN2000)";
    private const string PhuongPhapDo = "Toàn đạc điện tử";
    private const string MucDoChinhXac = "Cao";

    public int Write(IEnumerable<NewGcnEnvelope> envelopes, string outputPath, string templatePath)
    {
        var resolved = ResolveTemplate(templatePath);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"Không tìm thấy Excel template: {templatePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var wb = new XLWorkbook(resolved);
        var ws = wb.Worksheet(SheetName);

        // Dọn dữ liệu cũ từ dòng 5.
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 4;
        for (int r = 5; r <= lastRow; r++) ws.Row(r).Clear();

        // STT (cột A) = số thứ tự của GIẤY trong Excel: mỗi envelope một số, đánh 1, 2, 3… theo đúng
        // thứ tự ghi ra. KHÔNG suy STT từ số serial: giấy thiếu serial sẽ không làm tăng bộ đếm (giấy
        // sau lấy lại đúng số cũ → STT trùng), còn 2 file khác nhau trùng serial thì file sau tái dùng
        // STT của file trước (→ STT lùi số). Mọi dòng thửa/đồng sở hữu của cùng giấy dùng chung số này
        // và được merge ô ở cuối SyncEnvelope.
        // MỖI ENVELOPE = MỘT FILE PDF NGUỒN. Nhờ bất biến này mà phân biệt được hai ca dễ lẫn:
        //   - 1 giấy nhiều thửa  → các thửa nằm trong CÙNG một envelope ⇒ không bao giờ là trùng.
        //   - 2 file khác nhau đọc ra cùng serial → HAI envelope cùng số serial ⇒ đúng là trùng,
        //     bất kể số tờ/số thửa có giống nhau hay không.
        // Chỉ so nội dung sheet (như FindDuplicateParcelRows) thì KHÔNG tách được hai ca này: hai file
        // trùng serial mà khác thửa trông y hệt một giấy nhiều thửa.
        int rowsWritten = 0;
        int stt = 0;
        var serialOwners = new Dictionary<string, SerialOwner>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in envelopes)
            rowsWritten += SyncEnvelope(ws, env, ++stt, serialOwners);

        int lastUsed = ws.LastRowUsed()?.RowNumber() ?? 5;
        if (lastUsed >= 5) ws.Rows(5, lastUsed).Style.Alignment.WrapText = true;

        // ClosedXML 0.104.2 ném NotSupportedException ở PopulateAutoFilter khi template có sẵn AutoFilter
        // -> tắt AutoFilter trên mọi sheet trước khi lưu (workaround).
        foreach (var sheet in wb.Worksheets)
            if (sheet.AutoFilter is not null && sheet.AutoFilter.IsEnabled)
                sheet.AutoFilter.IsEnabled = false;

        wb.SaveAs(outputPath);
        return rowsWritten;
    }

    private int SyncEnvelope(
        IXLWorksheet ws, NewGcnEnvelope envelope, int stt, Dictionary<string, SerialOwner> serialOwners)
    {
        var sourceRows = envelope.danh_sach_dong ?? new List<NewGcnRow>();
        bool fallbackRow = sourceRows.Count == 0;
        var rows = fallbackRow ? new List<NewGcnRow> { new() } : sourceRows;

        string loaiQuanHe = (envelope.thong_tin_gcn.loai_quan_he ?? "").Trim().ToLowerInvariant();
        var owners = envelope.thong_tin_gcn.chu_su_dung_chi_tiet ?? new List<NewGcnOwner>();

        // Chốt TRƯỚC khi ghi dòng nào: file nguồn nào đã chiếm số serial này. Đọc sau khi ghi thì
        // chính envelope hiện tại đã nằm trong bảng và sẽ tự khớp với chính mình.
        string serialKey = RemoveSpaces(envelope.thong_tin_gcn.so_serial);
        SerialOwner? duplicateOfSource =
            serialKey.Length > 0 && serialOwners.TryGetValue(serialKey, out var existingOwner)
                ? existingOwner
                : null;

        int firstRow = -1, lastRow = -1;
        int rowsWritten = 0;

        foreach (var rowData in rows)
        {
            // Chụp trước khi ghi thửa này: các thửa khác của cùng GCN có bộ ba khác nên không tự khớp,
            // còn dòng đồng sở hữu bên dưới dùng lại đúng snapshot này để không khớp với dòng chính.
            var duplicateSerialRows = FindDuplicateParcelRows(ws, envelope.thong_tin_gcn.so_serial, rowData);

            int row = FindNextEmptyRow(ws);
            if (firstRow == -1) firstRow = row;
            lastRow = row;

            WriteEnvelopeRow(ws, row, stt, envelope, rowData);
            ApplyDuplicateParcelWarning(ws, row, envelope.thong_tin_gcn.so_serial, rowData, duplicateSerialRows);
            ApplyDuplicateSourceWarning(ws, row, envelope, duplicateOfSource);
            rowsWritten++;

            if (loaiQuanHe == "dong_su_dung" && owners.Count > 1)
            {
                for (int i = 1; i < owners.Count; i++)
                {
                    int coRow = FindNextEmptyRow(ws);
                    lastRow = coRow;
                    WriteCoOwnerRow(ws, coRow, stt, envelope, rowData, owners[i]);
                    ApplyDuplicateParcelWarning(ws, coRow, envelope.thong_tin_gcn.so_serial, rowData, duplicateSerialRows);
                    ApplyDuplicateSourceWarning(ws, coRow, envelope, duplicateOfSource);
                    rowsWritten++;
                }
            }
        }

        // File nguồn ĐẦU TIÊN mang số serial này giữ vai trò "chủ" — mọi file sau đọc ra cùng serial
        // đều được chỉ ngược về đúng dòng này, nên cảnh báo không trỏ lòng vòng lẫn nhau.
        if (serialKey.Length > 0 && firstRow != -1 && !serialOwners.ContainsKey(serialKey))
            serialOwners[serialKey] = new SerialOwner(firstRow, DescribeSourceFile(envelope));

        if (firstRow != -1 && lastRow > firstRow)
        {
            var range = ws.Range(firstRow, 1, lastRow, 1);
            range.Merge();
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        return rowsWritten;
    }

    private static int FindNextEmptyRow(IXLWorksheet ws)
    {
        int row = 5;
        while (true)
        {
            bool hasData = false;
            for (int col = 1; col <= 20; col++)
            {
                if (!ws.Cell(row, col).IsEmpty()) { hasData = true; break; }
            }
            if (!hasData) break;
            row++;
        }
        return row;
    }

    /// <summary>
    /// Tìm các dòng ĐÃ ghi trùng thửa với <paramref name="rowData"/>. Một GCN có nhiều thửa là nghiệp vụ
    /// bình thường (mỗi thửa 1 dòng, cùng số serial) nên KHÔNG được coi là trùng — chỉ báo trùng khi khớp
    /// cả bộ ba số serial (B) + số tờ (AR) + số thửa (AQ).
    /// Phải gọi TRƯỚC khi ghi dòng thửa hiện tại, vì dòng đồng sở hữu mang cùng bộ ba và sẽ tự khớp chính nó.
    /// </summary>
    private static List<int> FindDuplicateParcelRows(IXLWorksheet ws, string? serial, NewGcnRow rowData)
    {
        var rows = new List<int>();
        string serialKey = RemoveSpaces(serial);
        if (string.IsNullOrEmpty(serialKey)) return rows;

        string sheetKey = RemoveSpaces(rowData.td_so_to);
        string parcelKey = RemoveSpaces(rowData.td_so_thua);

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 4;
        for (int r = 5; r <= lastRow; r++)
        {
            if (!RemoveSpaces(ws.Cell(r, "B").Value.ToString()).Equals(serialKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!RemoveSpaces(ws.Cell(r, "AR").Value.ToString()).Equals(sheetKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!RemoveSpaces(ws.Cell(r, "AQ").Value.ToString()).Equals(parcelKey, StringComparison.OrdinalIgnoreCase))
                continue;
            rows.Add(r);
        }
        return rows;
    }

    private static void ApplyDuplicateParcelWarning(
        IXLWorksheet ws, int row, string? serial, NewGcnRow rowData, IReadOnlyList<int> duplicateRows)
    {
        if (duplicateRows.Count == 0) return;

        string normalized = NormalizeSerialDisplay(serial);
        if (string.IsNullOrEmpty(normalized)) return;

        AppendWarning(ws, row,
            $"Trùng số serial {normalized}{DescribeParcel(rowData)} với dòng {string.Join(", ", duplicateRows)}.");
    }

    /// <summary>
    /// Cảnh báo HAI FILE NGUỒN khác nhau đọc ra cùng một số serial. Khác hẳn cảnh báo trùng thửa ở trên:
    /// phép này KHÔNG nhìn số tờ / số thửa, nên bắt được cả trường hợp hai file trùng serial mà khác
    /// thửa — đúng cái trước đây lọt lưới vì trông y hệt một giấy nhiều thửa.
    /// </summary>
    private static void ApplyDuplicateSourceWarning(
        IXLWorksheet ws, int row, NewGcnEnvelope envelope, SerialOwner? duplicateOf)
    {
        if (duplicateOf is not { } owner) return;

        string normalized = NormalizeSerialDisplay(envelope.thong_tin_gcn.so_serial);
        if (string.IsNullOrEmpty(normalized)) return;

        AppendWarning(ws, row,
            $"Trùng số serial {normalized}: file \"{DescribeSourceFile(envelope)}\" đọc ra cùng số serial " +
            $"với file \"{owner.FileName}\" (dòng {owner.Row}). Đây là HAI FILE NGUỒN KHÁC NHAU, " +
            "không phải một giấy chứng nhận nhiều thửa.");
    }

    /// <summary>Gộp thêm một cảnh báo vào ô GL (giữ cảnh báo đã có) và tô đỏ dòng.</summary>
    private static void AppendWarning(IXLWorksheet ws, int row, string message)
    {
        var warningCell = ws.Cell(row, "GL");
        string currentWarning = warningCell.Value.ToString().Trim();
        warningCell.Value = string.IsNullOrEmpty(currentWarning)
            ? message
            : currentWarning + Environment.NewLine + message;
        warningCell.Style.Alignment.WrapText = true;
        ws.Row(row).Style.Fill.BackgroundColor = XLColor.Red;
    }

    private static string DescribeSourceFile(NewGcnEnvelope envelope)
    {
        var name = (envelope.ten_file ?? "").Trim();
        return name.Length == 0 ? "(không rõ tên file)" : name;
    }

    /// <summary>File nguồn ĐẦU TIÊN đã chiếm một số serial: dòng đầu của nó và tên file, để cảnh báo trỏ về.</summary>
    private readonly record struct SerialOwner(int Row, string FileName);

    /// <summary>Phần mô tả "tờ .., thửa .." để cảnh báo nói rõ trùng ở thửa nào (rỗng nếu không có dữ liệu).</summary>
    private static string DescribeParcel(NewGcnRow rowData)
    {
        var parts = new List<string>();
        var sheet = (rowData.td_so_to ?? "").Trim();
        var parcel = (rowData.td_so_thua ?? "").Trim();
        if (!string.IsNullOrEmpty(sheet)) parts.Add($"tờ {sheet}");
        if (!string.IsNullOrEmpty(parcel)) parts.Add($"thửa {parcel}");
        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    private static void WriteEnvelopeRow(IXLWorksheet ws, int row, int stt, NewGcnEnvelope envelope, NewGcnRow rowData)
    {
        var info = envelope.thong_tin_gcn;
        var owners = info.chu_su_dung_chi_tiet ?? new List<NewGcnOwner>();
        var firstOwner = owners.FirstOrDefault() ?? new NewGcnOwner();

        ws.Cell(row, "A").Value = stt;
        ws.Cell(row, "B").Value = info.so_serial ?? "";
        ws.Cell(row, "D").Value = info.ma_vach ?? "";
        ws.Cell(row, "F").Value = "GDC";
        ws.Cell(row, "G").Value = info.ma_ho_gia_dinh ?? "";

        string loaiQuanHe = (info.loai_quan_he ?? "").Trim().ToLowerInvariant();
        bool isVoChong = loaiQuanHe == "vo_chong" && owners.Count >= 2;

        if (isVoChong)
        {
            WriteOwnerBlock(ws, row, owners[0], "I", "J", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V");
            WriteOwnerBlock(ws, row, owners[1], "Z", "AA", "AC", "AD", "AE", "AF", "AG", "AH", "AI", "AJ", "AK", "AL", "AM");
            // AO (dân tộc vợ/chồng) để trống — không suy đoán dân tộc.
            ws.Cell(row, "AP").Value = QuocTich;
        }
        else
        {
            ws.Cell(row, "I").Value = loaiQuanHe == "dong_su_dung"
                ? (firstOwner.ho_ten ?? "")
                : string.Join("; ", owners.Select(c => c.ho_ten ?? ""));
            ws.Cell(row, "J").Value = firstOwner.gioi_tinh ?? "";
            ws.Cell(row, "L").Value = firstOwner.nam_sinh ?? "";
            ws.Cell(row, "M").Value = firstOwner.loai_giay_to ?? "";
            ws.Cell(row, "N").Value = firstOwner.so_giay_to ?? "";
            ws.Cell(row, "O").Value = firstOwner.ngay_cap ?? "";
            ws.Cell(row, "P").Value = firstOwner.noi_cap ?? "";
            ws.Cell(row, "Q").Value = firstOwner.so_nha_ngo ?? "";
            ws.Cell(row, "R").Value = firstOwner.duong_pho ?? "";
            ws.Cell(row, "S").Value = firstOwner.to_dan_pho ?? "";
            ws.Cell(row, "T").Value = firstOwner.ma_xa ?? "";
            ws.Cell(row, "U").Value = ComposeXaHuyenTinh(firstOwner);
            ws.Cell(row, "V").Value = firstOwner.dia_chi_day_du ?? "";
            ClearSpouseCols(ws, row);
        }

        // X (dân tộc) để trống — không suy đoán dân tộc của chủ sử dụng.
        ws.Cell(row, "Y").Value = QuocTich;

        WriteParcelAndCommon(ws, row, info, rowData);
    }

    private static void WriteCoOwnerRow(IXLWorksheet ws, int row, int stt, NewGcnEnvelope envelope, NewGcnRow rowData, NewGcnOwner owner)
    {
        var info = envelope.thong_tin_gcn;

        ws.Cell(row, "A").Value = stt;
        ws.Cell(row, "B").Value = info.so_serial ?? "";
        ws.Cell(row, "D").Value = info.ma_vach ?? "";
        ws.Cell(row, "F").Value = "GDC";
        ws.Cell(row, "G").Value = info.ma_ho_gia_dinh ?? "";
        WriteOwnerBlock(ws, row, owner, "I", "J", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V");
        // X (dân tộc) để trống — không suy đoán dân tộc của chủ sử dụng.
        ws.Cell(row, "Y").Value = QuocTich;
        ClearSpouseCols(ws, row);

        WriteParcelAndCommon(ws, row, info, rowData);
    }

    /// <summary>Ghi 1 khối chủ sử dụng: họ tên / năm sinh / giấy tờ / ngày cấp / nơi cấp + 6 cột địa chỉ (gồm mã xã — chỉ có khi giấy in ra).</summary>
    private static void WriteOwnerBlock(IXLWorksheet ws, int row, NewGcnOwner o,
        string cHoTen, string cGioiTinh, string cNamSinh, string cLoaiGt, string cSoGt, string cNgayCap, string cNoiCap,
        string cSoNha, string cDuong, string cToDp, string cMaXa, string cXaHT, string cDcDu)
    {
        ws.Cell(row, cHoTen).Value = o.ho_ten ?? "";
        ws.Cell(row, cGioiTinh).Value = o.gioi_tinh ?? "";
        ws.Cell(row, cNamSinh).Value = o.nam_sinh ?? "";
        ws.Cell(row, cLoaiGt).Value = o.loai_giay_to ?? "";
        ws.Cell(row, cSoGt).Value = o.so_giay_to ?? "";
        ws.Cell(row, cNgayCap).Value = o.ngay_cap ?? "";
        ws.Cell(row, cNoiCap).Value = o.noi_cap ?? "";
        ws.Cell(row, cSoNha).Value = o.so_nha_ngo ?? "";
        ws.Cell(row, cDuong).Value = o.duong_pho ?? "";
        ws.Cell(row, cToDp).Value = o.to_dan_pho ?? "";
        ws.Cell(row, cMaXa).Value = o.ma_xa ?? "";
        ws.Cell(row, cXaHT).Value = ComposeXaHuyenTinh(o);
        ws.Cell(row, cDcDu).Value = o.dia_chi_day_du ?? "";
    }

    /// <summary>
    /// Cột "Xã, huyện, tỉnh" (U của chủ / AL của vợ-chồng). Model tách tên xã ra trường RIÊNG
    /// <c>xa_phuong</c> và chỉ để huyện+tỉnh trong <c>xa_huyen_tinh</c>, nên ghi thẳng
    /// <c>xa_huyen_tinh</c> làm MẤT tên xã: cột "Xã, huyện, tỉnh" chỉ còn "huyện X - tỉnh Y" và
    /// <c>xa_phuong</c> không vào cột nào cả (khuôn này không có cột tên xã riêng).
    /// Ghép lại cho đúng nghĩa của cột. Cột "Mã xã/phường" (T/AK) vẫn để trống theo <c>ma_xa</c> —
    /// giấy không in mã xã và prompt cố ý không tra bảng địa phương.
    /// </summary>
    private static string ComposeXaHuyenTinh(NewGcnOwner o)
    {
        string xa = (o.xa_phuong ?? "").Trim();
        string huyenTinh = (o.xa_huyen_tinh ?? "").Trim();
        if (xa.Length == 0) return huyenTinh;
        if (huyenTinh.Length == 0) return xa;
        // Một số lượt model đã gộp sẵn tên xã vào chuỗi này → không nhân đôi.
        if (huyenTinh.Contains(xa, StringComparison.OrdinalIgnoreCase)) return huyenTinh;
        // Giữ đúng dấu phân cách mà model dùng trong chính chuỗi huyện/tỉnh của giấy này.
        string separator = huyenTinh.Contains(" - ", StringComparison.Ordinal) ? " - "
            : huyenTinh.Contains(", ", StringComparison.Ordinal) ? ", "
            : " - ";
        return xa + separator + huyenTinh;
    }

    private static void ClearSpouseCols(IXLWorksheet ws, int row)
    {
        foreach (var c in new[] { "Z", "AC", "AD", "AE", "AF", "AG", "AH", "AI", "AJ", "AK", "AL", "AM" })
            ws.Cell(row, c).Value = "";
    }

    /// <summary>Ghi thông tin thửa đất, đa MĐSD (1..4), địa chỉ thửa, thông tin ký, thay đổi, độ tin cậy, cảnh báo, GA.</summary>
    private static void WriteParcelAndCommon(IXLWorksheet ws, int row, NewGcnInfo info, NewGcnRow rowData)
    {
        ws.Cell(row, "C").Value = LastChars(info.ma_vach, 6);
        ws.Cell(row, "AQ").Value = rowData.td_so_thua ?? "";
        ws.Cell(row, "AR").Value = rowData.td_so_to ?? "";
        ws.Cell(row, "AT").Value = LoaiBanDo;
        ws.Cell(row, "AU").Value = rowData.ten_don_vi_do ?? "";
        ws.Cell(row, "AV").Value = PhuongPhapDo;
        ws.Cell(row, "AW").Value = MucDoChinhXac;
        ws.Cell(row, "AX").Value = rowData.ngay_hoan_thanh_do ?? "";
        ws.Cell(row, "AY").Value = rowData.pl_thua_dat ?? "";
        ws.Cell(row, "BA").Value = rowData.td_tong_dien_tich ?? "";

        var mdsdList = rowData.muc_dich_su_dung ?? new List<NewGcnMdsd>();

        var mdsd1 = mdsdList.ElementAtOrDefault(0);
        if (mdsd1 is not null)
        {
            ws.Cell(row, "BB").Value = mdsd1.ma_mdsd ?? "";
            ws.Cell(row, "BC").Value = mdsd1.ma_mdsd_quy_hoach ?? "";
            ws.Cell(row, "BD").Value = mdsd1.dien_tich ?? "";
            ws.Cell(row, "BE").Value = mdsd1.ma_htsd ?? "";
            ws.Cell(row, "BF").Value = mdsd1.thoi_han_su_dung ?? "";
            ws.Cell(row, "BG").Value = "";
            ws.Cell(row, "BH").Value = mdsd1.ma_ngsd ?? "";
            ws.Cell(row, "BI").Value = mdsd1.ten_ngsd ?? "";
        }
        else
        {
            ws.Cell(row, "BB").Value = "";
            ws.Cell(row, "BC").Value = "";
            ws.Cell(row, "BD").Value = "";
            ws.Cell(row, "BE").Value = "";
            ws.Cell(row, "BF").Value = "";
            ws.Cell(row, "BG").Value = "";
            ws.Cell(row, "BH").Value = "";
            ws.Cell(row, "BI").Value = "";
        }

        var mdsd2 = mdsdList.ElementAtOrDefault(1);
        if (mdsd2 is not null)
        {
            ws.Cell(row, "BJ").Value = mdsd2.ma_mdsd ?? "";
            ws.Cell(row, "BK").Value = mdsd2.ma_mdsd_quy_hoach ?? "";
            ws.Cell(row, "BL").Value = mdsd2.dien_tich ?? "";
            ws.Cell(row, "BM").Value = mdsd2.ma_htsd ?? "";
            ws.Cell(row, "BN").Value = mdsd2.thoi_han_su_dung ?? "";
            ws.Cell(row, "BO").Value = "";
            ws.Cell(row, "BP").Value = mdsd2.ma_ngsd ?? "";
            ws.Cell(row, "BQ").Value = mdsd2.ten_ngsd ?? "";
        }
        else
        {
            ws.Cell(row, "BJ").Value = "";
            ws.Cell(row, "BK").Value = "";
            ws.Cell(row, "BL").Value = "";
            ws.Cell(row, "BM").Value = "";
            ws.Cell(row, "BN").Value = "";
            ws.Cell(row, "BO").Value = "";
            ws.Cell(row, "BP").Value = "";
            ws.Cell(row, "BQ").Value = "";
        }

        var mdsd3 = mdsdList.ElementAtOrDefault(2);
        if (mdsd3 is not null)
        {
            ws.Cell(row, "BR").Value = mdsd3.ma_mdsd ?? "";
            ws.Cell(row, "BS").Value = mdsd3.ma_mdsd_quy_hoach ?? "";
            ws.Cell(row, "BT").Value = mdsd3.dien_tich ?? "";
            ws.Cell(row, "BU").Value = mdsd3.ma_htsd ?? "";
            ws.Cell(row, "BV").Value = mdsd3.thoi_han_su_dung ?? "";
            ws.Cell(row, "BW").Value = "";
            ws.Cell(row, "BX").Value = mdsd3.ma_ngsd ?? "";
            ws.Cell(row, "BY").Value = mdsd3.ten_ngsd ?? "";
        }
        else
        {
            ws.Cell(row, "BR").Value = "";
            ws.Cell(row, "BS").Value = "";
            ws.Cell(row, "BT").Value = "";
            ws.Cell(row, "BU").Value = "";
            ws.Cell(row, "BV").Value = "";
            ws.Cell(row, "BW").Value = "";
            ws.Cell(row, "BX").Value = "";
            ws.Cell(row, "BY").Value = "";
        }

        var mdsd4 = mdsdList.ElementAtOrDefault(3);
        if (mdsd4 is not null)
        {
            ws.Cell(row, "BZ").Value = mdsd4.ma_mdsd ?? "";
            ws.Cell(row, "CA").Value = mdsd4.ma_mdsd_quy_hoach ?? "";
            ws.Cell(row, "CB").Value = mdsd4.dien_tich ?? "";
            ws.Cell(row, "CC").Value = mdsd4.ma_htsd ?? "";
            ws.Cell(row, "CD").Value = mdsd4.thoi_han_su_dung ?? "";
            ws.Cell(row, "CE").Value = "";
            ws.Cell(row, "CF").Value = mdsd4.ma_ngsd ?? "";
            ws.Cell(row, "CG").Value = mdsd4.ten_ngsd ?? "";
        }
        else
        {
            ws.Cell(row, "BZ").Value = "";
            ws.Cell(row, "CA").Value = "";
            ws.Cell(row, "CB").Value = "";
            ws.Cell(row, "CC").Value = "";
            ws.Cell(row, "CD").Value = "";
            ws.Cell(row, "CE").Value = "";
            ws.Cell(row, "CF").Value = "";
            ws.Cell(row, "CG").Value = "";
        }

        ws.Cell(row, "CH").Value = rowData.dctd_so_nha_ngo ?? "";
        ws.Cell(row, "CI").Value = rowData.dctd_duong_pho ?? "";
        ws.Cell(row, "CN").Value = rowData.dctd_to_dan_pho ?? "";
        ws.Cell(row, "CO").Value = rowData.dctd_xa_huyen_tinh ?? "";
        ws.Cell(row, "CP").Value = rowData.dctd_dia_chi_day_du ?? "";

        ws.Cell(row, "CQ").Value = RemoveSpaces(info.so_serial);
        ws.Cell(row, "CU").Value = info.loai_gcn ?? "";
        ws.Cell(row, "CV").Value = rowData.ky_so_vao_so ?? "";
        ws.Cell(row, "CW").Value = rowData.ky_ngay_ky_gcn ?? "";
        ws.Cell(row, "CX").Value = rowData.ky_ngay_ky_gcn ?? "";
        ws.Cell(row, "CY").Value = rowData.ky_nguoi_ky ?? "";
        ws.Cell(row, "DB").Value = LastChars(rowData.ky_ngay_ky_gcn, 4);

        // Ba mục KHÁC NHAU trên giấy → ba cột khác nhau, không được trộn:
        //  DE "Ghi chú trang 1" ← ghi chú in trên mặt 1 (mặt có quốc hiệu + tiêu đề GCN), thường rỗng.
        //  DF "Ghi chú trang 2" ← mục "6. Ghi chú" (mẫu cũ, mặt 2) / "5. Ghi chú" (mẫu QR, trang 2).
        //  GD ← mục "Những thay đổi sau khi cấp GCN" (mẫu cũ: mặt 3 kéo dài sang mặt 4; mẫu QR: mục 6 trang 2).
        ws.Cell(row, "DE").Value = FormatStringLines(info.ghi_chu_trang_1);
        ws.Cell(row, "DE").Style.Alignment.WrapText = true;
        ws.Cell(row, "DF").Value = FormatStringLines(info.ghi_chu);
        ws.Cell(row, "DF").Style.Alignment.WrapText = true;
        ws.Cell(row, "GD").Value = FormatStringLines(info.thong_tin_thay_doi);
        ws.Cell(row, "GD").Style.Alignment.WrapText = true;

        ws.Cell(row, "GK").Value = info.do_tin_cay ?? "";

        ws.Cell(row, "GL").Value = FormatStringLines(info.canh_bao);
        ws.Cell(row, "GL").Style.Alignment.WrapText = true;

        string level = (info.do_tin_cay ?? "").Trim().ToLowerInvariant();
        if (level == "trung_binh") ws.Row(row).Style.Fill.BackgroundColor = XLColor.Yellow;
        else if (level == "thap") ws.Row(row).Style.Fill.BackgroundColor = XLColor.Red;

        static string FormatStringLines(List<string>? list)
        {
            if (list is null) return "";
            var lines = new List<string>();
            for (int i = 0; i < list.Count; i++)
                if (!string.IsNullOrEmpty(list[i])) lines.Add($"{lines.Count + 1}) {list[i]}");
            return string.Join("\n", lines);
        }

        ws.Cell(row, "GA").Value = NormalizeSerialDisplay(info.so_serial);
    }

    /// <summary>Bỏ toàn bộ khoảng trắng khỏi chuỗi (VD serial "AA 06654954" → "AA06654954").</summary>
    private static string RemoveSpaces(string? value)
        => string.IsNullOrEmpty(value) ? "" : new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    /// <summary>Lấy tối đa <paramref name="n"/> ký tự cuối của chuỗi (rỗng nếu không có dữ liệu).</summary>
    private static string LastChars(string? value, int n)
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) return "";
        return s.Length <= n ? s : s.Substring(s.Length - n);
    }

    private static string NormalizeSerialDisplay(string? serial)
        => string.Join(" ", (serial ?? "")
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ResolveTemplate(string templatePath)
    {
        if (string.IsNullOrEmpty(templatePath)) return templatePath;
        if (File.Exists(templatePath)) return templatePath;

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
