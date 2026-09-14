using System.Collections.Generic;

namespace OCR.Business.VbdBn;

/// <summary>
/// Schema JSON do LLM trả về cho luồng trích CCCD/CMND từ file GTK của màn OCR GCN VBD-BN.
/// Mỗi phần tử <see cref="danh_sach_giay_to"/> = MỘT giấy tờ tuỳ thân (mặt trước + mặt sau đã ghép).
/// File GTK không có giấy tờ tuỳ thân nào → danh sách rỗng, KHÔNG phải lỗi.
/// </summary>
public sealed class CccdEnvelope
{
    public string ten_file { get; set; } = "";
    public List<CccdRecord> danh_sach_giay_to { get; set; } = new();
}

/// <summary>Một CMND 9 số / CCCD 12 số / Thẻ Căn cước đọc được từ file GTK.</summary>
public sealed class CccdRecord
{
    /// <summary>"cmnd" | "cccd" | "can_cuoc".</summary>
    public string? loai_giay_to { get; set; }
    public string? so_giay_to { get; set; }
    public string? ho_ten { get; set; }
    /// <summary>dd/MM/yyyy.</summary>
    public string? ngay_sinh { get; set; }
    public string? gioi_tinh { get; set; }
    public string? quoc_tich { get; set; }
    public string? que_quan { get; set; }
    /// <summary>CCCD mẫu 2024 in "Nơi cư trú" — vẫn ghi vào trường này.</summary>
    public string? noi_thuong_tru { get; set; }
    /// <summary>dd/MM/yyyy.</summary>
    public string? ngay_cap { get; set; }
    public string? noi_cap { get; set; }
    /// <summary>dd/MM/yyyy; "không thời hạn" → null kèm dòng canh_bao.</summary>
    public string? co_gia_tri_den { get; set; }
    /// <summary>Các trang (1-based) trong PDF GTK chứa giấy tờ này.</summary>
    public List<int>? trang { get; set; }
    /// <summary>"cao" | "trung_binh" | "thap".</summary>
    public string? do_tin_cay { get; set; }
    public List<string>? canh_bao { get; set; }
}
