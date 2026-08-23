using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OCR.Business.Models;

/// <summary>
/// Schema JSON do LLM trả về cho pipeline "OCR GCN VietBD".
/// TÁCH RIÊNG khỏi <see cref="NewGcnEnvelope"/> của màn iLIS: hai màn dùng hai prompt khác nhau nên
/// schema phải tiến hoá độc lập — sửa prompt/schema bên này không được kéo theo màn kia.
///
/// Ba khác biệt so với schema iLIS, bám theo khuôn Excel_Template_VietBD:
///  1. <c>ma_loai_gcn</c> — prompt xuất THẲNG mã theo danh mục DM_LoaiGiayChungNhan của Việt Bản Đồ,
///     thay vì xuất tên nguyên văn rồi phần mềm tra bảng (bảng mã của VietBD đánh số khác iLIS).
///  2. Địa chỉ chủ tách sẵn <c>xa</c>/<c>huyen</c>/<c>tinh</c> thành ba trường — template có ba cột
///     riêng (AO/AP/AQ), không phải một chuỗi gộp như khuôn iLIS.
///  3. Không có bảng mã xã / bảng đơn vị đo đạc riêng của một địa phương: <c>ma_xa</c>,
///     <c>ten_don_vi_do</c>, <c>ngay_hoan_thanh_do</c> chỉ điền khi ĐỌC ĐƯỢC trên giấy.
/// </summary>
public class VietBdGcnEnvelope
{
    public string ten_file { get; set; } = "";

    /// <summary>
    /// Số GCN riêng biệt (số serial khác nhau) mà model thấy trong file nguồn. Nằm NGOÀI
    /// <see cref="thong_tin_gcn"/> vì đây là thuộc tính của FILE, không phải của một GCN.
    /// &gt;1 = PDF bị ghép nhiều GCN khi scan → phải tách bằng "Tách GCN" trước khi OCR.
    /// Xem <see cref="NewGcnEnvelope.so_luong_gcn_trong_file"/> — cùng lý do, hai màn giữ bản riêng.
    /// </summary>
    public int? so_luong_gcn_trong_file { get; set; }

    public VietBdGcnInfo thong_tin_gcn { get; set; } = new();
    public List<VietBdGcnRow> danh_sach_dong { get; set; } = new();
}

public class VietBdGcnInfo
{
    public string? so_serial { get; set; }
    public string? loai_mau { get; set; }

    /// <summary>Mã loại GCN theo danh mục DM_LoaiGiayChungNhan của Việt Bản Đồ (VD "11", "98").</summary>
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_loai_gcn { get; set; }

    /// <summary>Tên loại GCN nguyên văn — chỉ để người dùng đối chiếu, không ghi vào Excel.</summary>
    public string? ten_loai_gcn { get; set; }

    public string? loai_quan_he { get; set; }

    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_vach { get; set; }

    public int? so_luong_thua_dat_doc_duoc { get; set; }
    public VietBdGcnPageOrder? thu_tu_trang { get; set; }
    public List<VietBdGcnOwner>? chu_su_dung_chi_tiet { get; set; }

    /// <summary>Mục "Những thay đổi sau khi cấp Giấy chứng nhận" — ghi vào cột phụ `FH`, KHÔNG phải cột ghi chú.</summary>
    public List<string>? thong_tin_thay_doi { get; set; }

    /// <summary>Ghi chú in trên TRANG 1 (trang quốc hiệu + tiêu đề GCN) — cột `X` GCN_ghiChuTrang1. Thường rỗng.</summary>
    public List<string>? ghi_chu_trang_1 { get; set; }

    /// <summary>Ghi chú in trên TRANG 2 (mục "6. Ghi chú" mẫu cũ / "5. Ghi chú" mẫu QR) — cột `Y` GCN_ghiChuTrang2.</summary>
    public List<string>? ghi_chu { get; set; }
    public string? do_tin_cay { get; set; }
    public List<string>? canh_bao { get; set; }
}

public class VietBdGcnPageOrder
{
    public int? trang_chu_su_dung { get; set; }
    public int? trang_thua_dat { get; set; }
    public List<int>? trang_thay_doi { get; set; }
    public List<int>? trang_bo_qua { get; set; }
}

public class VietBdGcnOwner
{
    public string? ho_ten { get; set; }

    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? gioi_tinh { get; set; }

    public string? nam_sinh { get; set; }
    public string? loai_giay_to { get; set; }
    public string? so_giay_to { get; set; }
    public string? ngay_cap { get; set; }
    public string? noi_cap { get; set; }

    public string? so_nha_ngo { get; set; }
    public string? duong_pho { get; set; }
    public string? to_dan_pho { get; set; }

    /// <summary>Xã/phường/thị trấn/đặc khu — cột AO của template.</summary>
    public string? xa { get; set; }

    /// <summary>Huyện/quận/thị xã — cột AP. Mẫu QR bỏ cấp huyện thì để null.</summary>
    public string? huyen { get; set; }

    /// <summary>Tỉnh/thành phố — cột AQ.</summary>
    public string? tinh { get; set; }

    public string? dia_chi_day_du { get; set; }

    /// <summary>Mã xã của chủ — CHỈ điền khi giấy có in, không tra bảng địa phương.</summary>
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_xa { get; set; }
}

public class VietBdGcnRow
{
    public string? td_so_thua { get; set; }
    public string? td_so_to { get; set; }
    public string? td_tong_dien_tich { get; set; }

    /// <summary>Loại thửa đất A/B/C/D/E — cột CH (TD_loaiThuaDat) của template.</summary>
    public string? loai_thua_dat { get; set; }

    public List<VietBdGcnMdsd>? muc_dich_su_dung { get; set; }

    public string? dctd_so_nha_ngo { get; set; }
    public string? dctd_duong_pho { get; set; }
    public string? dctd_to_dan_pho { get; set; }
    public string? dctd_dia_chi_day_du { get; set; }

    /// <summary>
    /// Tài sản gắn liền với đất khi là NHÀ Ở RIÊNG LẺ (mục `3.` mẫu QR / mục `2. Nhà ở` mẫu cũ).
    /// Null khi thửa không có tài sản, hoặc tài sản thuộc loại chưa hỗ trợ (căn hộ, công trình xây dựng…).
    /// </summary>
    public VietBdGcnNhaO? nha_o { get; set; }

    public string? ten_don_vi_do { get; set; }
    public string? ngay_hoan_thanh_do { get; set; }

    public string? ky_so_vao_so { get; set; }
    public string? ky_ngay_vao_so { get; set; }
    public string? ky_ngay_ky_gcn { get; set; }
    public string? ky_nguoi_ky { get; set; }
}

/// <summary>
/// Nhà ở riêng lẻ gắn liền với thửa đất — đổ vào cụm cột `DE`…`DX` của khuôn Việt Bản Đồ.
/// Prompt trả THẲNG mã danh mục (<see cref="ma_loai_nha_rieng_le"/>, <see cref="ma_quyen_so_huu"/>)
/// giống cách làm với <c>ma_loai_gcn</c> — code không tra bảng.
/// </summary>
public class VietBdGcnNhaO
{
    /// <summary>Mã theo DM_LoaiNhaRiengLe (1 = Nhà ở riêng lẻ … 8 = Nhà ở độc lập) — cột `DE`.</summary>
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_loai_nha_rieng_le { get; set; }

    /// <summary>Quyền sở hữu: `0` = sở hữu riêng, `1` = sở hữu chung — cột `DG`.</summary>
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_quyen_so_huu { get; set; }

    /// <summary>Hình thức sở hữu nguyên văn ("Sở hữu chung"/"Sở hữu riêng") — chỉ để đối chiếu, không ghi Excel.</summary>
    public string? hinh_thuc_so_huu { get; set; }

    /// <summary>Tên tài sản nguyên văn trên giấy — cột `DH`.</summary>
    public string? ten_tai_san { get; set; }

    public string? dia_chi_day_du { get; set; }   // DI
    public string? so_nha { get; set; }           // DJ
    public string? duong_pho { get; set; }        // DK
    public string? to_dan_pho { get; set; }       // DL

    public string? dien_tich_san { get; set; }        // DM
    public string? dien_tich_su_dung { get; set; }    // DO
    public string? dien_tich_xay_dung { get; set; }   // DP

    public string? cap_hang { get; set; }         // DQ
    public string? ket_cau { get; set; }          // DR
    public string? so_tang { get; set; }          // DS
    public string? nam_xay_dung { get; set; }     // DV
    public string? nam_hoan_thanh { get; set; }   // DW
    public string? thoi_han_so_huu { get; set; }  // DX
}

public class VietBdGcnMdsd
{
    public string? ma_mdsd { get; set; }
    public string? ten_mdsd { get; set; }
    public string? ma_mdsd_quy_hoach { get; set; }
    public string? dien_tich { get; set; }
    public string? thoi_han_su_dung { get; set; }
    public string? ma_ngsd { get; set; }
    public string? ten_ngsd { get; set; }
}
