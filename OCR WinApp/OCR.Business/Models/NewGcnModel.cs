using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OCR.Business.Models;

/// <summary>Schema JSON do LLM trả về cho pipeline "OCR GCN (New)" (port từ GcnOcrApp.NewGcnModel).</summary>
public class NewGcnEnvelope
{
    public string ten_file { get; set; } = "";

    /// <summary>
    /// Số GCN riêng biệt (số serial khác nhau) mà model thấy trong file nguồn. Nằm NGOÀI
    /// <see cref="thong_tin_gcn"/> vì đây là thuộc tính của FILE, không phải của một GCN.
    /// &gt;1 = PDF bị ghép nhiều GCN khi scan → phải tách bằng "Tách GCN" trước khi OCR.
    ///
    /// Trước đây dấu hiệu nhận biết file ghép là model tự đổi đầu ra thành JSON array — một TÁC DỤNG PHỤ
    /// biến mất ngay khi bật `responseSchema` (constrained decoding ép đầu ra là object). Trường đếm
    /// tường minh này thay thế, và hoạt động ở mọi provider dù schema có được gửi hay không.
    /// </summary>
    public int? so_luong_gcn_trong_file { get; set; }

    public NewGcnInfo thong_tin_gcn { get; set; } = new();
    public List<NewGcnRow> danh_sach_dong { get; set; } = new();
}

public class NewGcnInfo
{
    public string? so_serial { get; set; }
    public string? loai_mau { get; set; }
    public string? loai_gcn { get; set; }
    public string? loai_quan_he { get; set; }
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_ho_gia_dinh { get; set; }
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_vach { get; set; }
    public int? so_luong_thua_dat_doc_duoc { get; set; }
    public NewGcnPageOrder? thu_tu_trang { get; set; }
    public List<NewGcnOwner>? chu_su_dung_chi_tiet { get; set; }

    /// <summary>Mục "Những thay đổi sau khi cấp Giấy chứng nhận" — ghi vào cột `GD`, KHÔNG phải cột ghi chú.</summary>
    public List<string>? thong_tin_thay_doi { get; set; }

    /// <summary>Ghi chú in trên TRANG 1 (trang quốc hiệu + tiêu đề GCN) — cột `DE` "Ghi chú trang 1". Thường rỗng.</summary>
    public List<string>? ghi_chu_trang_1 { get; set; }

    /// <summary>Ghi chú in trên TRANG 2 (mục "6. Ghi chú" mẫu cũ / "5. Ghi chú" mẫu QR) — cột `DF` "Ghi chú trang 2".</summary>
    public List<string>? ghi_chu { get; set; }
    public string? do_tin_cay { get; set; }
    public List<string>? canh_bao { get; set; }
}

public class NewGcnPageOrder
{
    public int? trang_chu_su_dung { get; set; }
    public int? trang_thua_dat { get; set; }
    public List<int>? trang_thay_doi { get; set; }
    public List<int>? trang_bo_qua { get; set; }
}

public class NewGcnOwner
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
    public string? xa_phuong { get; set; }
    public string? xa_huyen_tinh { get; set; }
    public string? dia_chi_day_du { get; set; }
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? ma_xa { get; set; }
}

public class NewGcnRow
{
    public string? td_so_thua { get; set; }
    public string? td_so_to { get; set; }
    public string? td_tong_dien_tich { get; set; }
    public string? pl_thua_dat { get; set; }

    public List<NewGcnMdsd>? muc_dich_su_dung { get; set; }

    public string? dctd_so_nha_ngo { get; set; }
    public string? dctd_duong_pho { get; set; }
    public string? dctd_to_dan_pho { get; set; }
    public string? dctd_xa_huyen_tinh { get; set; }
    public string? dctd_dia_chi_day_du { get; set; }

    public string? ten_don_vi_do { get; set; }
    public string? ngay_hoan_thanh_do { get; set; }

    public string? ky_so_vao_so { get; set; }
    public string? ky_ngay_vao_so { get; set; }
    public string? ky_ngay_ky_gcn { get; set; }
    public string? ky_nguoi_ky { get; set; }
}

public class NewGcnMdsd
{
    public string? ma_mdsd { get; set; }
    public string? ten_mdsd { get; set; }
    public string? ma_mdsd_quy_hoach { get; set; }
    public string? dien_tich { get; set; }
    public string? ma_htsd { get; set; }
    public string? ten_htsd { get; set; }
    public string? thoi_han_su_dung { get; set; }
    public string? ma_ngsd { get; set; }
    public string? ten_ngsd { get; set; }
}

public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => reader.GetString()
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
