using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using OCR.Business.Models;

namespace OCR.Business.VietBdGcn;

/// <summary>
/// Ghi <see cref="VietBdGcnEnvelope"/> ra Excel_Template_VietBD.xlsx (sheet "KeKhaiDangKy", từ dòng 5).
///
/// Đơn vị dòng KHÁC hẳn khuôn iLIS: iLIS gói tối đa 4 mục đích sử dụng vào 4 khối cột trên CÙNG một
/// dòng, còn Việt Bản Đồ tách MỖI mục đích sử dụng thành MỘT dòng riêng — mọi thông tin GCN/chủ/thửa
/// lặp lại, chỉ cụm CU–DD đổi theo từng MĐSD. Đồng sử dụng cũng đẻ thêm dòng cho từng chủ (template
/// chỉ có 2 khối chủ CHU_/VC_ nên không nhét được nhiều chủ vào một dòng).
///
/// Prompt riêng của màn này đã trả sẵn <c>ma_loai_gcn</c> (mã danh mục VietBD) và tách sẵn
/// <c>xa</c>/<c>huyen</c>/<c>tinh</c>, nên exporter KHÔNG còn phải tra bảng hay tự cắt chuỗi địa chỉ.
///
/// Ba cột phụ app tự thêm sau cột cuối <c>FE</c> của template: <c>FF</c> độ tin cậy, <c>FG</c> cảnh báo,
/// <c>FH</c> nội dung "Những thay đổi sau khi cấp GCN". Xoá ba cột này là file về đúng khuôn 161 cột.
/// </summary>
public sealed class VietBdGcnExcelExporter : IVietBdGcnExcelExporter
{
    private const string SheetName = "KeKhaiDangKy";

    private const int FirstDataRow = 5;

    /// <summary>Cột phụ do app thêm sau cột cuối cùng (FE) của template — xoá 3 cột này là về đúng khuôn 161 cột.</summary>
    private const string ColDoTinCay = "FF";
    private const string ColCanhBao = "FG";
    private const string ColThayDoi = "FH";

    // Giá trị hằng mặc định điền sẵn (không trích xuất từ giấy), ghi theo MÃ danh mục của Việt Bản Đồ.
    private const string LoaiDoiTuongSuDungDat = "CNV";  // DM_LoaiDoiTuongSuDungDat: cá nhân trong nước
    // KHÔNG điền sẵn dân tộc: GCN không in dân tộc nên mặc định "Kinh" chỉ là suy đoán, sai với chủ
    // sử dụng là người dân tộc thiểu số. Cột dân tộc (AG / BA) để trống cho người dùng tự điền.
    private const string MaQuocTich = "VNM";             // DM_QuocTich: Việt Nam
    private const string MaLoaiBanDo = "1";              // DM_LoaiBanDoDiaChinh: bản đồ địa chính VN2000
    private const string PhuongPhapDo = "Toàn đạc điện tử";
    private const string MucDoChinhXac = "Cao";

    private static readonly Regex DatePattern = new(@"^\d{1,2}/\d{1,2}/\d{4}$", RegexOptions.Compiled);

    public int Write(
        IEnumerable<VietBdGcnEnvelope> envelopes, string outputPath, string templatePath, string? maXa = null,
        Func<VietBdGcnEnvelope, string>? resolveTenFile = null)
    {
        // Mã xã do người dùng nhập một lần cho cả lô, LUÔN đè lên mọi thứ đọc được (giấy không in mã xã
        // của thửa đất nên OCR không bao giờ có giá trị này).
        var maXaValue = (maXa ?? "").Trim();
        var resolved = ResolveTemplate(templatePath);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"Không tìm thấy Excel template: {templatePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var wb = new XLWorkbook(resolved);
        var ws = wb.Worksheet(SheetName);

        // Dọn dữ liệu cũ từ dòng 5.
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        for (int r = FirstDataRow; r <= lastRow; r++) ws.Row(r).Clear();

        WriteExtraColumnHeaders(ws);

        // Cột D (DDK_ngayTiepNhan) = NGÀY CHẠY export, giống nhau cho mọi dòng của cùng một lần Xuất —
        // chốt MỘT LẦN ở đây, không gọi lại DateTime.Now cho từng dòng.
        var ngayChay = DateTime.Now.ToString("dd/MM/yyyy");

        // Ghi tuần tự, KHÔNG dò ô trống như màn iLIS: mỗi thửa ở đây sinh nhiều dòng liên tiếp nên
        // con trỏ dòng tự tăng là đủ và rẻ hơn nhiều lần quét lại lưới.
        int row = FirstDataRow;
        int stt = 1;
        foreach (var env in envelopes)
        {
            var tenFile = resolveTenFile is not null ? resolveTenFile(env) : env.ten_file;
            WriteEnvelope(ws, env, maXaValue, tenFile, ngayChay, ref row, ref stt);
        }

        int rowsWritten = row - FirstDataRow;
        if (rowsWritten > 0) ws.Rows(FirstDataRow, row - 1).Style.Alignment.WrapText = true;

        // ClosedXML 0.104.2 ném NotSupportedException ở PopulateAutoFilter khi template có sẵn AutoFilter.
        foreach (var sheet in wb.Worksheets)
            if (sheet.AutoFilter is not null && sheet.AutoFilter.IsEnabled)
                sheet.AutoFilter.IsEnabled = false;

        wb.SaveAs(outputPath);
        return rowsWritten;
    }

    private static void WriteExtraColumnHeaders(IXLWorksheet ws)
    {
        // Dòng 1 = hàng mã trường, dòng 3 = hàng tên tiếng Việt (khớp bố cục header của template).
        ws.Cell(1, ColDoTinCay).Value = "EXTRA_doTinCay";
        ws.Cell(1, ColCanhBao).Value = "EXTRA_canhBao";
        ws.Cell(1, ColThayDoi).Value = "EXTRA_thongTinThayDoi";
        ws.Cell(3, ColDoTinCay).Value = "Độ tin cậy";
        ws.Cell(3, ColCanhBao).Value = "Cảnh báo";
        ws.Cell(3, ColThayDoi).Value = "Những thay đổi sau khi cấp GCN";
    }

    private static void WriteEnvelope(
        IXLWorksheet ws, VietBdGcnEnvelope envelope, string maXa, string tenFile, string ngayChay,
        ref int row, ref int stt)
    {
        var info = envelope.thong_tin_gcn;
        var parcels = envelope.danh_sach_dong ?? new List<VietBdGcnRow>();
        if (parcels.Count == 0) parcels = new List<VietBdGcnRow> { new() };

        string loaiQuanHe = (info.loai_quan_he ?? "").Trim().ToLowerInvariant();
        var owners = info.chu_su_dung_chi_tiet ?? new List<VietBdGcnOwner>();
        bool isDongSuDung = loaiQuanHe == "dong_su_dung" && owners.Count > 1;
        bool isVoChong = loaiQuanHe == "vo_chong" && owners.Count >= 2;

        // Mỗi dòng mang đúng một chủ ở khối CHU_ (đồng sử dụng), hoặc cặp vợ chồng CHU_ + VC_.
        var ownerBlocks = isDongSuDung
            ? owners.Select(o => (Chu: o, Vo: (VietBdGcnOwner?)null)).ToList()
            : new List<(VietBdGcnOwner Chu, VietBdGcnOwner? Vo)>
            {
                (owners.FirstOrDefault() ?? new VietBdGcnOwner(), isVoChong ? owners[1] : null)
            };

        foreach (var parcel in parcels)
        {
            // Chụp danh sách dòng trùng TRƯỚC khi ghi cả cụm dòng của thửa này: các dòng MĐSD/đồng sử dụng
            // sinh ra bên dưới mang cùng bộ ba serial+tờ+thửa nên sẽ tự khớp chính chúng nếu dò lại sau.
            var duplicateRows = FindDuplicateParcelRows(ws, info.so_serial, parcel, row);

            var mdsdList = parcel.muc_dich_su_dung ?? new List<VietBdGcnMdsd>();
            var mdsdRows = mdsdList.Count == 0 ? new List<VietBdGcnMdsd?> { null } : mdsdList.Cast<VietBdGcnMdsd?>().ToList();

            foreach (var mdsd in mdsdRows)
            {
                foreach (var (chu, vo) in ownerBlocks)
                {
                    WriteRow(ws, row, stt, info, parcel, mdsd, chu, vo, isVoChong, isDongSuDung, tenFile, maXa, ngayChay);
                    ApplyWarnings(ws, row, info, parcel, duplicateRows);
                    row++;
                    stt++;
                }
            }
        }
    }

    private static void WriteRow(
        IXLWorksheet ws, int row, int stt, VietBdGcnInfo info, VietBdGcnRow parcel, VietBdGcnMdsd? mdsd,
        VietBdGcnOwner chu, VietBdGcnOwner? vo, bool isVoChong, bool isDongSuDung, string? tenFile, string maXa,
        string ngayChay)
    {
        // --- Đơn đăng ký & Giấy chứng nhận ---
        ws.Cell(row, "A").Value = stt;
        // Cột B (DDK_maXa): lấy từ hộp thoại lúc bấm Bắt đầu, KHÔNG lấy từ OCR.
        ws.Cell(row, "B").Value = maXa;
        // Cột C (DDK_maDon): CHỦ Ý dùng chung giá trị với cột N (số serial có dấu cách) — chốt với chủ
        // dự án 26/08/2026, thay hẳn logic "{số tờ}-{số thửa}" cũ (BuildMaDon, đã bỏ).
        var serialCoCach = NormalizeSpacing(info.so_serial);
        ws.Cell(row, "C").Value = serialCoCach;
        // Cột D (DDK_ngayTiepNhan): ngày CHẠY export (không phải ngày trên giấy), một giá trị cho cả lô.
        ws.Cell(row, "D").Value = ngayChay;
        ws.Cell(row, "M").Value = isDongSuDung ? "1" : "0";
        // Cột N (GCN_soPhatHanh): giữ dấu cách như OCR đọc được (VD "CA 332417") — KHÔNG RemoveSpaces.
        ws.Cell(row, "N").Value = serialCoCach;
        ws.Cell(row, "O").Value = parcel.ky_so_vao_so ?? "";
        ws.Cell(row, "Q").Value = LastChars(info.ma_vach, 6);
        ws.Cell(row, "R").Value = parcel.ky_ngay_ky_gcn ?? "";
        ws.Cell(row, "S").Value = parcel.ky_nguoi_ky ?? "";
        ws.Cell(row, "T").Value = info.ma_vach ?? "";
        // Cột I (DDK_thoiDiemDangKy): ngày cấp GCN = ngày ký (đã có sẵn ở cột R, cùng nguồn OCR).
        ws.Cell(row, "I").Value = parcel.ky_ngay_ky_gcn ?? "";
        // Cột L (DDK_dieuKienCapGiay): mặc định "0" — chốt với chủ dự án 26/08/2026, không đọc từ giấy.
        ws.Cell(row, "L").Value = "0";
        // Cột U (GCN_donViCap): cấp hành chính cơ quan ký, model đã phân loại sẵn ở ma_don_vi_cap.
        ws.Cell(row, "U").Value = MapDonViCap(parcel.ma_don_vi_cap);
        // Prompt riêng của VietBD trả THẲNG mã danh mục — không còn bảng map tên → mã trong code.
        ws.Cell(row, "W").Value = (info.ma_loai_gcn ?? "").Trim();
        // Ba mục KHÁC NHAU trên giấy → ba cột khác nhau, không được trộn:
        //  X "GCN_ghiChuTrang1" ← ghi chú in trên mặt 1 (mặt có quốc hiệu + tiêu đề GCN), thường rỗng.
        //  Y "GCN_ghiChuTrang2" ← mục "6. Ghi chú" (mẫu cũ, mặt 2) / "5. Ghi chú" (mẫu QR, trang 2).
        //  FH (cột phụ) ← mục "Những thay đổi sau khi cấp GCN" (mẫu cũ: mặt 3 kéo dài sang mặt 4; mẫu QR: mục 6 trang 2).
        ws.Cell(row, "X").Value = FormatStringLines(info.ghi_chu_trang_1);
        ws.Cell(row, "Y").Value = FormatStringLines(info.ghi_chu);

        // --- Chủ sử dụng / đại diện ---
        ws.Cell(row, "Z").Value = isVoChong ? "Vợ chồng" : "Cá nhân";
        ws.Cell(row, "AA").Value = LoaiDoiTuongSuDungDat;
        WriteOwnerBlock(ws, row, chu, "AB", "AC", "AE", "AH", "AJ", "AK", "AL", "AM", "AN", "AO", "AP", "AQ",
            "AR", "AS", "AT", "AU");

        // --- Vợ/chồng (chỉ khi loai_quan_he = vo_chong) ---
        if (vo is not null)
        {
            WriteOwnerBlock(ws, row, vo, "AV", "AW", "AY", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BK",
                "BL", "BM", "BN", "BO");
        }
        else
        {
            ClearSpouseBlock(ws, row);
        }

        // --- Tài liệu đo đạc ---
        ws.Cell(row, "CB").Value = MaLoaiBanDo;
        ws.Cell(row, "CC").Value = parcel.ten_don_vi_do ?? "";
        ws.Cell(row, "CD").Value = PhuongPhapDo;
        ws.Cell(row, "CE").Value = MucDoChinhXac;
        ws.Cell(row, "CG").Value = parcel.ngay_hoan_thanh_do ?? "";

        // --- Thửa đất ---
        ws.Cell(row, "CH").Value = (parcel.loai_thua_dat ?? "").Trim();
        ws.Cell(row, "CI").Value = parcel.td_so_thua ?? "";
        ws.Cell(row, "CJ").Value = parcel.td_so_to ?? "";
        ws.Cell(row, "CN").Value = parcel.td_tong_dien_tich ?? "";
        ws.Cell(row, "CO").Value = parcel.dctd_dia_chi_day_du ?? "";
        ws.Cell(row, "CP").Value = parcel.dctd_so_nha_ngo ?? "";
        ws.Cell(row, "CQ").Value = parcel.dctd_duong_pho ?? "";
        ws.Cell(row, "CR").Value = parcel.dctd_to_dan_pho ?? "";

        // --- Mục đích sử dụng & nguồn gốc: cụm DUY NHẤT đổi giữa các dòng của cùng một thửa ---
        string thoiHan = mdsd?.thoi_han_su_dung ?? "";
        ws.Cell(row, "CU").Value = mdsd?.ma_mdsd ?? "";
        ws.Cell(row, "CV").Value = mdsd?.ma_mdsd_quy_hoach ?? "";
        ws.Cell(row, "CX").Value = mdsd?.dien_tich ?? "";
        ws.Cell(row, "CY").Value = IsDateLike(thoiHan) ? thoiHan : "";
        ws.Cell(row, "CZ").Value = thoiHan;
        ws.Cell(row, "DA").Value = mdsd?.ma_ngsd ?? "";
        ws.Cell(row, "DC").Value = mdsd?.dien_tich ?? "";

        // --- Nhà ở riêng lẻ gắn liền với thửa (DE…DX) ---
        // Nhà thuộc về THỬA nên lặp lại trên MỌI dòng của thửa đó (giống sheet KeKhaiDangKy_Mau:
        // hai dòng cùng thửa khác MĐSD vẫn mang y hệt dữ liệu nhà). Thửa không có nhà → cả cụm để trống.
        WriteNhaOBlock(ws, row, parcel.nha_o);

        // --- Hồ sơ + 3 cột phụ ---
        ws.Cell(row, "FE").Value = tenFile ?? "";
        ws.Cell(row, ColDoTinCay).Value = info.do_tin_cay ?? "";
        ws.Cell(row, ColThayDoi).Value = FormatStringLines(info.thong_tin_thay_doi);
        ws.Cell(row, ColThayDoi).Style.Alignment.WrapText = true;
    }

    /// <summary>Ghi một khối chủ (họ tên → nơi cấp giấy tờ). Dùng chung cho khối CHU_ và khối VC_.</summary>
    private static void WriteOwnerBlock(
        IXLWorksheet ws, int row, VietBdGcnOwner o,
        string cHoTen, string cNgaySinh, string cGioiTinh, string cQuocTich,
        string cDiaChi, string cSoNha, string cDuongPho, string cToDanPho, string cMaXa,
        string cTenXa, string cTenHuyen, string cTenTinh,
        string cLoaiGt, string cSoGt, string cNgayCap, string cNoiCap)
    {
        ws.Cell(row, cHoTen).Value = o.ho_ten ?? "";
        ws.Cell(row, cNgaySinh).Value = o.nam_sinh ?? "";
        ws.Cell(row, cGioiTinh).Value = o.gioi_tinh ?? "";
        // Cột dân tộc (AG của chủ / BA của vợ chồng) để trống — không suy đoán dân tộc.
        ws.Cell(row, cQuocTich).Value = MaQuocTich;
        ws.Cell(row, cDiaChi).Value = o.dia_chi_day_du ?? "";
        ws.Cell(row, cSoNha).Value = o.so_nha_ngo ?? "";
        ws.Cell(row, cDuongPho).Value = o.duong_pho ?? "";
        ws.Cell(row, cToDanPho).Value = o.to_dan_pho ?? "";
        ws.Cell(row, cMaXa).Value = o.ma_xa ?? "";
        // Prompt riêng đã tách sẵn ba cấp — exporter không tự cắt chuỗi địa chỉ nữa.
        ws.Cell(row, cTenXa).Value = o.xa ?? "";
        ws.Cell(row, cTenHuyen).Value = o.huyen ?? "";
        ws.Cell(row, cTenTinh).Value = o.tinh ?? "";
        ws.Cell(row, cLoaiGt).Value = o.loai_giay_to ?? "";
        ws.Cell(row, cSoGt).Value = o.so_giay_to ?? "";
        ws.Cell(row, cNgayCap).Value = o.ngay_cap ?? "";
        ws.Cell(row, cNoiCap).Value = o.noi_cap ?? "";
    }

    /// <summary>
    /// Ghi cụm nhà ở riêng lẻ (DE…DX). Prompt đã tra sẵn mã danh mục nên exporter chỉ chép lại.
    /// Các cột giấy không in (DF loại nhà chung cư, DN diện tích sàn phụ, DT số tầng hầm,
    /// DU tổng số căn — của nhà chung cư) luôn để trống.
    /// </summary>
    private static void WriteNhaOBlock(IXLWorksheet ws, int row, VietBdGcnNhaO? nha)
    {
        ws.Cell(row, "DE").Value = (nha?.ma_loai_nha_rieng_le ?? "").Trim();
        ws.Cell(row, "DG").Value = (nha?.ma_quyen_so_huu ?? "").Trim();
        ws.Cell(row, "DH").Value = nha?.ten_tai_san ?? "";
        ws.Cell(row, "DI").Value = nha?.dia_chi_day_du ?? "";
        ws.Cell(row, "DJ").Value = nha?.so_nha ?? "";
        ws.Cell(row, "DK").Value = nha?.duong_pho ?? "";
        ws.Cell(row, "DL").Value = nha?.to_dan_pho ?? "";
        ws.Cell(row, "DM").Value = nha?.dien_tich_san ?? "";
        ws.Cell(row, "DO").Value = nha?.dien_tich_su_dung ?? "";
        ws.Cell(row, "DP").Value = nha?.dien_tich_xay_dung ?? "";
        ws.Cell(row, "DQ").Value = nha?.cap_hang ?? "";
        ws.Cell(row, "DR").Value = nha?.ket_cau ?? "";
        ws.Cell(row, "DS").Value = nha?.so_tang ?? "";
        ws.Cell(row, "DV").Value = nha?.nam_xay_dung ?? "";
        ws.Cell(row, "DW").Value = nha?.nam_hoan_thanh ?? "";
        ws.Cell(row, "DX").Value = nha?.thoi_han_so_huu ?? "";
    }

    private static void ClearSpouseBlock(IXLWorksheet ws, int row)
    {
        foreach (var c in new[]
                 {
                     "AV", "AW", "AY", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BK",
                     "BL", "BM", "BN", "BO"
                 })
            ws.Cell(row, c).Value = "";
    }

    /// <summary>
    /// Chuẩn hoá khoảng trắng của số serial (VD "CA  332417" hay "CA332417" đọc lệch) về đúng một
    /// khoảng trắng giữa phần chữ và phần số, dùng cho cột C/N — KHÔNG xoá hẳn dấu cách như
    /// <see cref="RemoveSpaces"/> (hàm đó vẫn giữ để so khớp trùng thửa nội bộ, xem <see cref="FindDuplicateParcelRows"/>).
    /// </summary>
    private static string NormalizeSpacing(string? value)
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) return "";
        return Regex.Replace(s, @"\s+", " ");
    }

    /// <summary>Mã cấp hành chính cơ quan cấp cho cột U (GCN_donViCap): huyện→0, tỉnh→1, sở→2.</summary>
    private static string MapDonViCap(string? maDonViCap) => (maDonViCap ?? "").Trim().ToLowerInvariant() switch
    {
        "huyen" => "0",
        "tinh" => "1",
        "so" => "2",
        _ => ""
    };

    /// <summary>
    /// Tìm các dòng ĐÃ ghi trùng thửa: khớp CẢ BA số phát hành (N) + số tờ (CJ) + số thửa (CI).
    /// Một GCN nhiều thửa, một thửa nhiều MĐSD hay nhiều chủ đồng sử dụng đều là nghiệp vụ bình thường,
    /// không phải trùng — nên chỉ quét tới trước dòng đang chuẩn bị ghi.
    /// </summary>
    private static List<int> FindDuplicateParcelRows(IXLWorksheet ws, string? serial, VietBdGcnRow parcel, int currentRow)
    {
        var rows = new List<int>();
        string serialKey = RemoveSpaces(serial);
        if (string.IsNullOrEmpty(serialKey)) return rows;

        string sheetKey = RemoveSpaces(parcel.td_so_to);
        string parcelKey = RemoveSpaces(parcel.td_so_thua);

        for (int r = FirstDataRow; r < currentRow; r++)
        {
            if (!RemoveSpaces(ws.Cell(r, "N").Value.ToString()).Equals(serialKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!RemoveSpaces(ws.Cell(r, "CJ").Value.ToString()).Equals(sheetKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!RemoveSpaces(ws.Cell(r, "CI").Value.ToString()).Equals(parcelKey, StringComparison.OrdinalIgnoreCase))
                continue;
            rows.Add(r);
        }
        return rows;
    }

    private static void ApplyWarnings(
        IXLWorksheet ws, int row, VietBdGcnInfo info, VietBdGcnRow parcel, IReadOnlyList<int> duplicateRows)
    {
        var lines = new List<string>();
        var modelWarning = FormatStringLines(info.canh_bao);
        if (!string.IsNullOrEmpty(modelWarning)) lines.Add(modelWarning);

        bool duplicate = duplicateRows.Count > 0;
        if (duplicate)
        {
            string normalized = NormalizeSerialDisplay(info.so_serial);
            if (!string.IsNullOrEmpty(normalized))
                lines.Add($"Trùng số serial {normalized}{DescribeParcel(parcel)} với dòng {string.Join(", ", duplicateRows)}.");
            else
                duplicate = false;
        }

        if (lines.Count > 0)
        {
            var cell = ws.Cell(row, ColCanhBao);
            cell.Value = string.Join(Environment.NewLine, lines);
            cell.Style.Alignment.WrapText = true;
        }

        string level = (info.do_tin_cay ?? "").Trim().ToLowerInvariant();
        if (duplicate || level == "thap") ws.Row(row).Style.Fill.BackgroundColor = XLColor.Red;
        else if (level == "trung_binh") ws.Row(row).Style.Fill.BackgroundColor = XLColor.Yellow;
    }

    /// <summary>Phần mô tả "tờ .., thửa .." để cảnh báo nói rõ trùng ở thửa nào (rỗng nếu không có dữ liệu).</summary>
    private static string DescribeParcel(VietBdGcnRow parcel)
    {
        var parts = new List<string>();
        var sheet = (parcel.td_so_to ?? "").Trim();
        var number = (parcel.td_so_thua ?? "").Trim();
        if (!string.IsNullOrEmpty(sheet)) parts.Add($"tờ {sheet}");
        if (!string.IsNullOrEmpty(number)) parts.Add($"thửa {number}");
        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    private static bool IsDateLike(string value) => DatePattern.IsMatch(value.Trim());

    private static string FormatStringLines(List<string>? list)
    {
        if (list is null) return "";
        var lines = new List<string>();
        for (int i = 0; i < list.Count; i++)
            if (!string.IsNullOrEmpty(list[i])) lines.Add($"{lines.Count + 1}) {list[i]}");
        return string.Join("\n", lines);
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
