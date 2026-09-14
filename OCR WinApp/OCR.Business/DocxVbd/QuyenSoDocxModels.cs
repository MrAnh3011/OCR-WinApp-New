using System.Collections.Generic;

namespace OCR.Business.DocxVbd;

/// <summary>
/// Một TRANG của sổ cấp GCN trong file docx (mỗi trang = một bảng <c>w:tbl</c> = một GCN):
/// mục I (tối đa 2 người sử dụng đất), mục II (các dòng thửa đất 11 cột) và mục III (biến động).
/// Đây là dữ liệu THÔ đọc nguyên văn từ docx — mọi quy tắc chuẩn hoá/quy đổi nằm ở
/// <see cref="DocxVbdEnvelopeMapper"/>, không nằm ở tầng đọc.
/// </summary>
public sealed class QuyenSoDocxPage
{
    /// <summary>Số trang in trên đầu trang ("Trang số: 08" → "08"); null khi không đọc được.</summary>
    public string? PageLabel { get; set; }

    /// <summary>Thứ tự bảng GCN trong file (1-based) — dùng thay <see cref="PageLabel"/> khi thiếu.</summary>
    public int TableIndex { get; set; }

    /// <summary>Mục I — tối đa 2 người ("1." và "2." của mẫu sổ, cặp vợ chồng khi có cả hai).</summary>
    public List<QuyenSoNguoiSuDung> NguoiSuDung { get; } = new();

    /// <summary>Mục II — mỗi dòng dữ liệu (không tính dòng header) là một thửa đất.</summary>
    public List<QuyenSoThuaDatRow> ThuaDat { get; } = new();

    /// <summary>Mục III — những thay đổi trong quá trình sử dụng đất (thường trống).</summary>
    public List<QuyenSoBienDongRow> BienDong { get; } = new();

    /// <summary>Cảnh báo phát sinh ngay lúc ĐỌC cấu trúc (VD không tách được mục I).</summary>
    public List<string> CanhBaoDoc { get; } = new();
}

/// <summary>Một người ở mục I — giữ nguyên văn, chưa tách địa chỉ/loại giấy tờ.</summary>
public sealed class QuyenSoNguoiSuDung
{
    public string HoTen { get; set; } = "";
    public string NamSinh { get; set; } = "";

    /// <summary>Số in sau nhãn "Số CMTND/CCCD/CMQĐ/HC".</summary>
    public string SoGiayTo { get; set; } = "";

    public string DiaChi { get; set; } = "";

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(HoTen) && string.IsNullOrWhiteSpace(NamSinh)
        && string.IsNullOrWhiteSpace(SoGiayTo) && string.IsNullOrWhiteSpace(DiaChi);
}

/// <summary>Một dòng dữ liệu mục II — 11 cột theo đúng thứ tự in trên mẫu sổ.</summary>
public sealed class QuyenSoThuaDatRow
{
    public string NgayVaoSo { get; set; } = "";
    public string SoThua { get; set; } = "";
    public string SoTo { get; set; } = "";
    public string DienTichRieng { get; set; } = "";
    public string DienTichChung { get; set; } = "";
    public string MucDichSuDung { get; set; } = "";
    public string ThoiHanSuDung { get; set; } = "";
    public string NguonGocSuDung { get; set; } = "";
    public string SoPhatHanh { get; set; } = "";
    public string SoVaoSo { get; set; } = "";

    /// <summary>Cột "Ghi chú" của mục II — chốt với chủ dự án 28/08/2026: BỎ, không ghi vào Excel.</summary>
    public string GhiChu { get; set; } = "";
}

/// <summary>Một dòng mục III — "Số thứ tự thửa đất | Ngày tháng năm | Nội dung ghi chú hoặc biến động".</summary>
public sealed class QuyenSoBienDongRow
{
    public string SoThua { get; set; } = "";
    public string NgayThang { get; set; } = "";
    public string NoiDung { get; set; } = "";
}
