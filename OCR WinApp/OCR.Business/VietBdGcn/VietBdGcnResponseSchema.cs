namespace OCR.Business.VietBdGcn;

/// <summary>
/// LỚP 2 — Schema đầu ra (định dạng Google AI Studio, type IN HOA) cho màn **OCR GCN VietBD**.
/// Cùng lý do với <see cref="NewGcn.NewGcnResponseSchema"/>: bật constrained decoding để model không
/// xuất number ở các trường dạng MÃ (`ma_vach`, `ma_loai_gcn`, `ma_xa`, `ma_loai_nha_rieng_le`…) làm
/// hỏng cú pháp JSON, và buộc model luôn khai báo <c>so_luong_gcn_trong_file</c>.
///
/// Là BẢN RIÊNG, KHÔNG dùng chung với schema iLIS: hai màn có hai prompt/model khác nhau
/// (<see cref="Models.VietBdGcnEnvelope"/> có thêm <c>ma_loai_gcn</c>, <c>nha_o</c>, tách sẵn
/// <c>xa</c>/<c>huyen</c>/<c>tinh</c>) nên schema phải tiến hoá độc lập.
///
/// ⚠️ Sửa <see cref="Models.VietBdGcnEnvelope"/> thì phải sửa cả file này.
/// </summary>
internal static class VietBdGcnResponseSchema
{
    /// <summary>Schema bất biến — dựng một lần rồi dùng lại cho mọi file (không mutate sau khi dựng).</summary>
    public static object Instance { get; } = Build();

    private static Dictionary<string, object> Str() => new() { ["type"] = "STRING", ["nullable"] = true };

    private static Dictionary<string, object> Int() => new() { ["type"] = "INTEGER", ["nullable"] = true };

    private static Dictionary<string, object> Arr(object item) => new() { ["type"] = "ARRAY", ["items"] = item };

    private static Dictionary<string, object> StrArr()
        => Arr(new Dictionary<string, object> { ["type"] = "STRING" });

    /// <summary>
    /// ⚠️ <c>propertyOrdering</c> là BẮT BUỘC ở MỌI object — xem giải thích đầy đủ ở
    /// <see cref="NewGcn.NewGcnResponseSchema"/>: `properties` là map trong proto của Google API nên
    /// mất thứ tự khai báo, thiếu ordering thì model sinh key xáo trộn và bỏ trắng gần hết trường
    /// (kể cả <c>chu_su_dung_chi_tiet</c>) mà không hề báo lỗi.
    /// </summary>
    private static Dictionary<string, object> Obj(
        Dictionary<string, object> properties, string[]? required = null, bool nullable = false)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "OBJECT",
            ["properties"] = properties,
            ["propertyOrdering"] = properties.Keys.ToArray()
        };
        if (required is { Length: > 0 }) schema["required"] = required;
        if (nullable) schema["nullable"] = true;
        return schema;
    }

    private static Dictionary<string, object> Strings(params string[] names)
    {
        var properties = new Dictionary<string, object>();
        foreach (var name in names) properties[name] = Str();
        return properties;
    }

    public static object Build()
    {
        var pageOrder = Obj(new Dictionary<string, object>
        {
            ["trang_chu_su_dung"] = Int(),
            ["trang_thua_dat"] = Int(),
            ["trang_thay_doi"] = Arr(new Dictionary<string, object> { ["type"] = "INTEGER" }),
            ["trang_bo_qua"] = Arr(new Dictionary<string, object> { ["type"] = "INTEGER" })
        }, nullable: true);

        // Khuôn VietBD có ba cột tên xã/huyện/tỉnh riêng (AO/AP/AQ) nên prompt tách sẵn ba trường.
        var owner = Obj(Strings(
            "ho_ten", "gioi_tinh", "nam_sinh", "loai_giay_to", "so_giay_to", "ngay_cap", "noi_cap",
            "so_nha_ngo", "duong_pho", "to_dan_pho", "xa", "huyen", "tinh", "dia_chi_day_du", "ma_xa"),
            required: ["ho_ten"]);

        var mdsd = Obj(Strings(
            "ma_mdsd", "ten_mdsd", "ma_mdsd_quy_hoach", "dien_tich", "thoi_han_su_dung",
            "ma_ngsd", "ten_ngsd"));

        // Nhà ở riêng lẻ gắn liền với thửa (cụm cột DE…DX). Hai mã danh mục phải là STRING.
        var nhaO = Obj(Strings(
            "ma_loai_nha_rieng_le", "ma_quyen_so_huu", "hinh_thuc_so_huu", "ten_tai_san",
            "dia_chi_day_du", "so_nha", "duong_pho", "to_dan_pho",
            "dien_tich_san", "dien_tich_su_dung", "dien_tich_xay_dung",
            "cap_hang", "ket_cau", "so_tang", "nam_xay_dung", "nam_hoan_thanh", "thoi_han_so_huu"),
            nullable: true);

        var rowProperties = Strings(
            "td_so_thua", "td_so_to", "td_tong_dien_tich", "loai_thua_dat",
            "dctd_so_nha_ngo", "dctd_duong_pho", "dctd_to_dan_pho", "dctd_dia_chi_day_du",
            "ten_don_vi_do", "ngay_hoan_thanh_do",
            "ky_so_vao_so", "ky_ngay_vao_so", "ky_ngay_ky_gcn", "ky_nguoi_ky");
        rowProperties["muc_dich_su_dung"] = Arr(mdsd);
        rowProperties["nha_o"] = nhaO;
        var row = Obj(rowProperties, required: ["td_so_thua", "td_so_to", "td_tong_dien_tich", "muc_dich_su_dung"]);

        var infoProperties = Strings(
            "so_serial", "loai_mau", "ma_loai_gcn", "ten_loai_gcn", "loai_quan_he",
            "ma_vach", "do_tin_cay");
        infoProperties["so_luong_thua_dat_doc_duoc"] = Int();
        infoProperties["thu_tu_trang"] = pageOrder;
        infoProperties["chu_su_dung_chi_tiet"] = Arr(owner);
        infoProperties["thong_tin_thay_doi"] = StrArr();
        infoProperties["ghi_chu_trang_1"] = StrArr();
        infoProperties["ghi_chu"] = StrArr();
        infoProperties["canh_bao"] = StrArr();

        // Cùng lý do với schema iLIS: chu_su_dung_chi_tiet + so_luong_thua_dat_doc_duoc phải LUÔN có
        // mặt (giá trị vẫn được null) để model không lặng lẽ bỏ qua chủ sử dụng.
        var info = Obj(infoProperties,
            required: ["so_serial", "so_luong_thua_dat_doc_duoc", "chu_su_dung_chi_tiet"]);

        return Obj(new Dictionary<string, object>
        {
            ["ten_file"] = Str(),
            ["so_luong_gcn_trong_file"] = new Dictionary<string, object> { ["type"] = "INTEGER" },
            ["thong_tin_gcn"] = info,
            ["danh_sach_dong"] = Arr(row)
        }, required: ["so_luong_gcn_trong_file", "thong_tin_gcn", "danh_sach_dong"]);
    }
}
