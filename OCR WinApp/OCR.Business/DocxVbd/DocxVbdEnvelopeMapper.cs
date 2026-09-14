using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OCR.Business.IlisUb;
using OCR.Business.Models;

namespace OCR.Business.DocxVbd;

/// <summary>
/// Map một trang sổ (<see cref="QuyenSoDocxPage"/>) thành <see cref="VietBdGcnEnvelope"/> — dùng chung
/// model + <c>IVietBdGcnExcelExporter</c> + template Excel_Template_VietBD.xlsx với màn OCR GCN VietBD,
/// nhưng mọi giá trị SUY LUẬN BẰNG CODE thay vì AI. Các quy tắc đã chốt với chủ dự án 28/08/2026:
///  • "Ngày tháng năm vào sổ" → ngày cấp (cột R, chuẩn hoá dd/MM/yyyy); "Số phát hành GCN QSDĐ" →
///    serial (N/C); "Số vào sổ cấp GCN QSDĐ" → số vào sổ (O); cột "Ghi chú" của mục II BỎ HẲN.
///  • Số thửa/số tờ CHỈ LẤY SỐ ("Lô 49"→"49", "K7"→"7"); ô nhiều số giữ cả dãy + cảnh báo; ô không có
///    chữ số hoặc mảnh trích đo (MTĐĐC) giữ nguyên văn + cảnh báo.
///  • Nguồn gốc ghi NGUYÊN VĂN mã đọc được; mã ngoài danh mục Việt Bản Đồ chỉ cộng cảnh báo.
///  • Mục I: có người "2." → cặp vợ chồng (khối CHU_ + VC_), chỉ người "1." → cá nhân. Sổ không in
///    giới tính/ngày cấp giấy tờ nên các cột đó để trống, KHÔNG suy đoán.
///  • Mục III → <c>thong_tin_thay_doi</c> (cột phụ FH "Những thay đổi sau khi cấp GCN").
///  • <c>ma_loai_gcn</c> tự suy từ ngày cấp theo đúng mốc BẢNG LOẠI GCN VIỆT BẢN ĐỒ trong
///    PROMPT_TRICH_XUAT_GCN_VIETBD.md (serial chỉ là căn cứ phụ khi thiếu ngày).
/// Cảnh báo dồn vào <c>canh_bao</c> (cột FG); <c>do_tin_cay</c> để trống vì không có tầng AI.
/// </summary>
public static class DocxVbdEnvelopeMapper
{
    /// <summary>Danh mục mã nguồn gốc của Việt Bản Đồ (BẢNG MÃ NGUỒN GỐC trong prompt VietBD).</summary>
    private static readonly HashSet<string> KnownNguonGocCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CNQ-CTT", "CNQ-KTT", "CNQ", "DT-THN", "DT-TML", "DG-CTT", "DG-KTT", "DG-QL",
        "DT-KCN", "DT-KCN-THN", "DT-KCN-TML",
        "NCQ-1", "NCQ-2", "NCQ-3", "NCQ-4", "NCQ-5", "NCQ-6", "NCQ-7", "NCQ-8", "NCQ-9",
        "NCQ-10", "NCQ-11", "NCQ-12", "NCQ-13", "NCQ-14", "NCQ-15", "NCQ-16", "NCQ-17"
    };

    private static readonly Regex DigitsPattern = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{4})$", RegexOptions.Compiled);
    private static readonly Regex DateAnywherePattern = new(@"(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{4})", RegexOptions.Compiled);

    public static VietBdGcnEnvelope Map(QuyenSoDocxPage page, string sourceFileName)
    {
        var warnings = new List<string>(page.CanhBaoDoc);
        var trang = string.IsNullOrWhiteSpace(page.PageLabel) ? $"#{page.TableIndex}" : page.PageLabel!;

        var owners = page.NguoiSuDung.Select(n => ToOwner(n, warnings)).ToList();
        if (owners.Count == 0)
            warnings.Add("Mục I: không đọc được người sử dụng đất nào.");

        // Gom dòng NỐI TIẾP vào thửa cha: sổ in "một thửa nhiều mục đích sử dụng" bằng một dòng cha
        // (tổng diện tích, có thể bỏ trống MĐSD) + các dòng ngay dưới chỉ có diện tích/MĐSD/thời hạn.
        var parcels = GroupParcels(page.ThuaDat, warnings);
        var rows = new List<VietBdGcnRow>();
        foreach (var parcel in parcels)
            rows.Add(ToRow(parcel.Parent, parcel.Continuations, parcel.ViTri, warnings));
        if (rows.Count == 0)
            warnings.Add("Mục II: không đọc được dòng thửa đất nào.");

        // Mỗi trang sổ = MỘT GCN (chốt với chủ dự án). Trang có nhiều số phát hành khác nhau là bất
        // thường: vẫn xuất theo số đầu tiên nhưng phải cảnh báo để người dùng tự rà.
        var serials = page.ThuaDat
            .Select(r => Collapse(r.SoPhatHanh))
            .Where(s => s.Length > 0)
            .ToList();
        var distinctSerials = serials
            .GroupBy(s => Regex.Replace(s, @"\s+", "").ToUpperInvariant())
            .Select(g => g.First())
            .ToList();
        if (distinctSerials.Count > 1)
            warnings.Add($"Trang có {distinctSerials.Count} số phát hành GCN khác nhau ({string.Join("; ", distinctSerials)}) — chỉ ghi số đầu tiên, cần kiểm tra thủ công.");
        var serial = distinctSerials.FirstOrDefault() ?? "";

        // Nhiều số vào sổ khác nhau = dấu hiệu một trang gộp NHIỀU GCN (thực tế gặp ở sổ An Lạc):
        // số vào sổ/ngày cấp vẫn đúng theo từng dòng, nhưng serial + loại GCN là mức trang nên phải rà tay.
        var distinctSoVaoSo = page.ThuaDat
            .Select(r => Collapse(r.SoVaoSo))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctSoVaoSo.Count > 1)
            warnings.Add($"Trang có {distinctSoVaoSo.Count} số vào sổ khác nhau ({string.Join("; ", distinctSoVaoSo)}) — có thể gồm nhiều GCN trên một trang, cần kiểm tra thủ công.");

        var ngayCap = page.ThuaDat.Select(r => ParseNgay(r.NgayVaoSo)).FirstOrDefault(d => d is not null);
        var maLoaiGcn = ClassifyLoaiGcn(ngayCap, serial, warnings);

        var thayDoi = page.BienDong.Select(FormatBienDong).Where(s => s.Length > 0).ToList();

        return new VietBdGcnEnvelope
        {
            ten_file = $"{sourceFileName} - Trang {trang}",
            so_luong_gcn_trong_file = 1,
            thong_tin_gcn = new VietBdGcnInfo
            {
                so_serial = serial,
                ma_loai_gcn = maLoaiGcn,
                loai_quan_he = owners.Count >= 2 ? "vo_chong" : "ca_nhan",
                so_luong_thua_dat_doc_duoc = rows.Count,
                chu_su_dung_chi_tiet = owners,
                thong_tin_thay_doi = thayDoi,
                ghi_chu_trang_1 = new List<string>(),
                ghi_chu = new List<string>(),
                do_tin_cay = null,
                canh_bao = warnings
            },
            danh_sach_dong = rows
        };
    }

    private static VietBdGcnOwner ToOwner(QuyenSoNguoiSuDung nguoi, List<string> warnings)
    {
        var owner = new VietBdGcnOwner
        {
            ho_ten = nguoi.HoTen,
            nam_sinh = nguoi.NamSinh,
            dia_chi_day_du = nguoi.DiaChi
            // Sổ không in giới tính/ngày cấp/nơi cấp giấy tờ → để trống, không suy đoán.
        };

        var soGiayTo = Regex.Replace(nguoi.SoGiayTo, @"\s+", "");
        if (soGiayTo.Length > 0)
        {
            owner.so_giay_to = soGiayTo;
            // Nhãn trên sổ là "Số CMTND/CCCD/CMQĐ/HC" — phân loại theo độ dài chuẩn của từng loại.
            if (soGiayTo.Length == 9 && soGiayTo.All(char.IsDigit)) owner.loai_giay_to = "CMND";
            else if (soGiayTo.Length == 12 && soGiayTo.All(char.IsDigit)) owner.loai_giay_to = "CCCD";
            else warnings.Add($"Số giấy tờ '{soGiayTo}' của {DisplayName(nguoi)} không đúng dạng CMND 9 số / CCCD 12 số — bỏ trống loại giấy tờ.");
        }

        SplitDiaChi(nguoi.DiaChi, owner);
        return owner;
    }

    private static string DisplayName(QuyenSoNguoiSuDung nguoi) =>
        string.IsNullOrWhiteSpace(nguoi.HoTen) ? "(không rõ tên)" : nguoi.HoTen;

    /// <summary>
    /// Tách "Thôn Nà Ó, xã An Lạc, huyện Sơn Động, tỉnh Bắc Giang" thành tổ dân phố / xã / huyện /
    /// tỉnh theo NHÃN đứng đầu từng đoạn (giữ nguyên văn cả nhãn). Đoạn đứng trước phần xã mà không
    /// có nhãn hành chính → thôn/khu (tổ dân phố). Không tách được phần nào thì để trống phần đó —
    /// địa chỉ đầy đủ luôn còn nguyên ở <c>dia_chi_day_du</c>.
    /// </summary>
    internal static void SplitDiaChi(string? diaChi, VietBdGcnOwner owner)
    {
        if (string.IsNullOrWhiteSpace(diaChi)) return;

        var parts = diaChi.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var leading = new List<string>();
        foreach (var part in parts)
        {
            var key = GcnFolderRules.Normalize(part);
            if (key.StartsWith("XA ", StringComparison.Ordinal)
                || key.StartsWith("PHUONG ", StringComparison.Ordinal)
                || key.StartsWith("THI TRAN ", StringComparison.Ordinal)
                || key.StartsWith("DAC KHU ", StringComparison.Ordinal))
            {
                owner.xa ??= part;
            }
            else if (key.StartsWith("HUYEN ", StringComparison.Ordinal)
                     || key.StartsWith("QUAN ", StringComparison.Ordinal)
                     || key.StartsWith("THI XA ", StringComparison.Ordinal))
            {
                owner.huyen ??= part;
            }
            else if (key.StartsWith("TINH ", StringComparison.Ordinal))
            {
                owner.tinh ??= part;
            }
            else if (key.StartsWith("THANH PHO ", StringComparison.Ordinal))
            {
                // "Thành phố" có thể là cấp huyện hoặc cấp tỉnh: nếu là đoạn CUỐI (không còn phần
                // tỉnh phía sau) coi là tỉnh/thành phố trực thuộc TW, ngược lại là cấp huyện.
                if (ReferenceEquals(part, parts[^1]) && owner.tinh is null) owner.tinh = part;
                else owner.huyen ??= part;
            }
            else if (owner.xa is null && owner.huyen is null && owner.tinh is null)
            {
                leading.Add(part);
            }
        }

        if (leading.Count > 0) owner.to_dan_pho = string.Join(", ", leading);
    }

    /// <summary>
    /// Dòng NỐI TIẾP: không có ngày/số thửa/số tờ/serial/số vào sổ nhưng còn diện tích, MĐSD, thời hạn
    /// hoặc nguồn gốc — là phần "một thửa nhiều mục đích sử dụng" in tách dòng ngay dưới dòng cha.
    /// </summary>
    private static bool IsContinuation(QuyenSoThuaDatRow row) =>
        Collapse(row.NgayVaoSo).Length == 0
        && Collapse(row.SoThua).Length == 0
        && Collapse(row.SoTo).Length == 0
        && Collapse(row.SoPhatHanh).Length == 0
        && Collapse(row.SoVaoSo).Length == 0
        && (Collapse(row.DienTichRieng).Length > 0 || Collapse(row.DienTichChung).Length > 0
            || Collapse(row.MucDichSuDung).Length > 0 || Collapse(row.ThoiHanSuDung).Length > 0
            || Collapse(row.NguonGocSuDung).Length > 0);

    private sealed record ParcelGroup(QuyenSoThuaDatRow Parent, List<QuyenSoThuaDatRow> Continuations, string ViTri);

    private static List<ParcelGroup> GroupParcels(List<QuyenSoThuaDatRow> rawRows, List<string> warnings)
    {
        var parcels = new List<ParcelGroup>();
        for (int i = 0; i < rawRows.Count; i++)
        {
            var raw = rawRows[i];
            if (IsContinuation(raw) && parcels.Count > 0)
            {
                parcels[^1].Continuations.Add(raw);
                continue;
            }
            if (IsContinuation(raw))
                warnings.Add($"Dòng thửa {i + 1}: dòng nối tiếp không có dòng cha phía trên — đọc như một thửa riêng.");
            parcels.Add(new ParcelGroup(raw, new List<QuyenSoThuaDatRow>(), $"Dòng thửa {i + 1}"));
        }
        return parcels;
    }

    private static VietBdGcnRow ToRow(
        QuyenSoThuaDatRow row, List<QuyenSoThuaDatRow> continuations, string viTri, List<string> warnings)
    {
        var (ngay, ngayIsDate) = NormalizeNgay(row.NgayVaoSo);
        if (ngay.Length == 0) warnings.Add($"{viTri}: thiếu \"Ngày tháng năm vào sổ\" (cột ngày cấp sẽ trống).");
        else if (!ngayIsDate) warnings.Add($"{viTri}: \"Ngày tháng năm vào sổ\" = '{ngay}' không đúng dạng ngày — ghi nguyên văn.");

        var soThua = ExtractSoHieu(row.SoThua, "số thửa", viTri, warnings);
        var soTo = ExtractSoHieu(row.SoTo, "số tờ", viTri, warnings);

        // Diện tích của dòng cha = TỔNG diện tích thửa (khi có dòng nối tiếp, các phần theo MĐSD nằm
        // ở từng dòng nối tiếp — đúng khuôn VietBD: tổng lặp lại ở CN, phần riêng từng MĐSD ở CX/DC).
        var dienTich = PickDienTich(row, viTri, warnings, required: true);

        var nguonGocCha = Collapse(row.NguonGocSuDung);
        ValidateNguonGoc(nguonGocCha, viTri, warnings);

        var maMdsdCha = Collapse(row.MucDichSuDung);
        var entries = new List<VietBdGcnMdsd>();
        if (maMdsdCha.Length > 0 || continuations.Count == 0)
        {
            // Dòng cha tự mang MĐSD (trường hợp phổ biến 1 thửa 1 MĐSD), hoặc không có dòng nối tiếp
            // nào thì vẫn giữ một mục để diện tích/thời hạn/nguồn gốc không bị mất.
            if (maMdsdCha.Length == 0)
                warnings.Add($"{viTri}: thiếu mục đích sử dụng đất.");
            entries.Add(new VietBdGcnMdsd
            {
                ma_mdsd = maMdsdCha,
                dien_tich = dienTich,
                thoi_han_su_dung = NormalizeThoiHan(row.ThoiHanSuDung),
                ma_ngsd = nguonGocCha,
                ten_ngsd = nguonGocCha
            });
        }

        foreach (var cont in continuations)
        {
            var maMdsd = Collapse(cont.MucDichSuDung);
            if (maMdsd.Length == 0)
                warnings.Add($"{viTri}: một dòng nối tiếp thiếu mục đích sử dụng đất.");
            var nguonGoc = Collapse(cont.NguonGocSuDung);
            if (nguonGoc.Length == 0) nguonGoc = nguonGocCha; // nguồn gốc chung cả thửa in ở dòng cha
            else ValidateNguonGoc(nguonGoc, viTri, warnings);

            entries.Add(new VietBdGcnMdsd
            {
                ma_mdsd = maMdsd,
                dien_tich = PickDienTich(cont, viTri, warnings, required: false),
                thoi_han_su_dung = NormalizeThoiHan(cont.ThoiHanSuDung),
                ma_ngsd = nguonGoc,
                ten_ngsd = nguonGoc
            });
        }

        return new VietBdGcnRow
        {
            td_so_thua = soThua,
            td_so_to = soTo,
            td_tong_dien_tich = dienTich,
            // Sổ chỉ ghi GCN đã cấp, không in thông tin tài sản gắn liền → loại thửa mặc định "A"
            // (đã cấp GCN, chưa có tài sản gắn liền) theo đúng quy tắc của prompt VietBD.
            loai_thua_dat = "A",
            muc_dich_su_dung = entries,
            ky_so_vao_so = Collapse(row.SoVaoSo),
            ky_ngay_vao_so = ngay,
            // "Ngày tháng năm vào sổ" = ngày cấp GCN (cột R) — chốt với chủ dự án 28/08/2026.
            ky_ngay_ky_gcn = ngay
            // Sổ không in cụm ký/cơ quan cấp → ky_nguoi_ky/ma_don_vi_cap để trống.
        };
    }

    /// <summary>Chọn diện tích của một dòng: ưu tiên cột Riêng, chỉ có cột Chung thì dùng kèm cảnh báo.</summary>
    private static string PickDienTich(QuyenSoThuaDatRow row, string viTri, List<string> warnings, bool required)
    {
        var rieng = Collapse(row.DienTichRieng);
        var chung = Collapse(row.DienTichChung);
        if (rieng.Length == 0 && chung.Length > 0)
        {
            warnings.Add($"{viTri}: chỉ có diện tích sử dụng CHUNG ({chung}) — dùng giá trị này, cần kiểm tra thủ công.");
            return chung;
        }
        if (rieng.Length > 0 && chung.Length > 0)
            warnings.Add($"{viTri}: có cả diện tích riêng ({rieng}) và chung ({chung}) — chỉ ghi diện tích riêng.");
        else if (rieng.Length == 0 && required)
            warnings.Add($"{viTri}: thiếu diện tích sử dụng.");
        return rieng;
    }

    private static void ValidateNguonGoc(string nguonGoc, string viTri, List<string> warnings)
    {
        foreach (var token in nguonGoc.Split(';', ',').Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            if (!KnownNguonGocCodes.Contains(token))
                warnings.Add($"{viTri}: mã nguồn gốc '{token}' không thuộc danh mục Việt Bản Đồ — ghi nguyên văn, cần kiểm tra thủ công.");
        }
    }

    /// <summary>
    /// "Chỉ lấy số" cho số thửa/số tờ: "Lô 49"→"49", "K7"→"7", "1 (a)"→"1". Ô chứa NHIỀU số
    /// ("13,14", "Lô 17; 11", "78-79-80") giữ cả dãy số + dấu phân cách và cảnh báo; ô KHÔNG có chữ số
    /// ("A", "G"…) hoặc mảnh trích đo địa chính ("MTĐĐC 06-2017") giữ nguyên văn + cảnh báo — cả hai
    /// quy tắc đã chốt với chủ dự án 28/08/2026.
    /// </summary>
    internal static string ExtractSoHieu(string raw, string tenTruong, string viTri, List<string> warnings)
    {
        var value = Collapse(raw);
        if (value.Length == 0) return "";

        var key = GcnFolderRules.Normalize(value).Replace(" ", "");
        if (key.Contains("MTDDC", StringComparison.Ordinal) || key.Contains("TRICHDO", StringComparison.Ordinal))
        {
            warnings.Add($"{viTri}: {tenTruong} '{value}' là mảnh trích đo địa chính — giữ nguyên văn, cần kiểm tra thủ công.");
            return value;
        }

        var digits = DigitsPattern.Matches(value);
        if (digits.Count == 0)
        {
            warnings.Add($"{viTri}: {tenTruong} '{value}' không có chữ số — giữ nguyên văn, cần kiểm tra thủ công.");
            return value;
        }
        if (digits.Count == 1) return digits[0].Value;

        var stripped = Regex.Replace(value, @"[^\d,;+\-/\s]", "");
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim(' ', ',', ';', '-', '+', '/');
        warnings.Add($"{viTri}: {tenTruong} '{value}' chứa nhiều số — giữ cả dãy '{stripped}', cần tách thủ công.");
        return stripped;
    }

    /// <summary>Chuẩn hoá ngày về dd/MM/yyyy ("30/5/2012" → "30/05/2012"); không parse được thì trả nguyên văn.</summary>
    internal static (string Value, bool IsDate) NormalizeNgay(string raw)
    {
        var value = Collapse(raw);
        if (value.Length == 0) return ("", false);

        var m = DatePattern.Match(value);
        if (!m.Success) return (value, false);

        int day = int.Parse(m.Groups[1].Value), month = int.Parse(m.Groups[2].Value), year = int.Parse(m.Groups[3].Value);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return (value, false);

        return ($"{day:00}/{month:00}/{year:0000}", true);
    }

    internal static DateTime? ParseNgay(string raw)
    {
        var (value, isDate) = NormalizeNgay(raw);
        if (!isDate) return null;
        var parts = value.Split('/');
        return new DateTime(int.Parse(parts[2]), int.Parse(parts[1]), int.Parse(parts[0]));
    }

    /// <summary>
    /// Thời hạn là MỐC NGÀY cụ thể ("Đến ngày 31/12/2060") → tách riêng ngày "31/12/2060" (đúng quy
    /// tắc prompt VietBD, để exporter tự đổ vào cột CY); dạng khác ("Đến năm 2063", "Lâu dài") giữ nguyên văn.
    /// </summary>
    internal static string NormalizeThoiHan(string raw)
    {
        var value = Collapse(raw);
        if (value.Length == 0) return "";

        var key = GcnFolderRules.Normalize(value).Replace(" ", "");
        var m = DateAnywherePattern.Match(value);
        if (!key.StartsWith("DENNGAY", StringComparison.Ordinal) || !m.Success) return value;

        var (ngay, isDate) = NormalizeNgay(m.Value);
        return isDate ? ngay : value;
    }

    /// <summary>
    /// Suy <c>ma_loai_gcn</c> theo BẢNG LOẠI GCN VIỆT BẢN ĐỒ: căn cứ CHÍNH là ngày cấp, serial chỉ
    /// dùng khi thiếu ngày (cùng thứ tự ưu tiên với prompt VietBD).
    /// </summary>
    internal static string? ClassifyLoaiGcn(DateTime? ngayCap, string? serial, List<string> warnings)
    {
        if (ngayCap is DateTime d)
        {
            if (d >= new DateTime(2025, 1, 1)) return "98";
            if (d >= new DateTime(2014, 7, 1)) return "11";
            if (d >= new DateTime(2009, 12, 10)) return "6";
            if (d >= new DateTime(2004, 7, 1)) return "1";
            return "2";
        }

        var s = Collapse(serial ?? "");
        var m = Regex.Match(s, @"^([A-Za-z]{1,2})\s*\d");
        if (!m.Success)
        {
            warnings.Add("Không xác định được loại GCN: thiếu cả ngày vào sổ lẫn số phát hành hợp lệ.");
            return null;
        }

        var letters = m.Groups[1].Value.ToUpperInvariant();
        if (letters.Length == 1) return "2";
        switch (letters[0])
        {
            case 'A': return "1";
            case 'C': return "11";
            case 'B':
                warnings.Add($"Serial '{s}' đầu 'B' nhưng thiếu ngày vào sổ — không phân biệt được loại GCN 6/11, tạm chọn 11.");
                return "11";
            default:
                warnings.Add($"Không xác định được loại GCN từ serial '{s}'.");
                return null;
        }
    }

    /// <summary>Một dòng mục III → một chuỗi "Thửa X, ngày Y: nội dung" cho cột FH.</summary>
    internal static string FormatBienDong(QuyenSoBienDongRow row)
    {
        var prefix = new List<string>();
        var soThua = Collapse(row.SoThua);
        var ngay = Collapse(row.NgayThang);
        var noiDung = Collapse(row.NoiDung);
        if (soThua.Length > 0) prefix.Add($"Thửa {soThua}");
        if (ngay.Length > 0) prefix.Add($"ngày {ngay}");

        if (prefix.Count == 0) return noiDung;
        return noiDung.Length == 0 ? string.Join(", ", prefix) : $"{string.Join(", ", prefix)}: {noiDung}";
    }

    private static string Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value, @"\s+", " ").Trim();
}
