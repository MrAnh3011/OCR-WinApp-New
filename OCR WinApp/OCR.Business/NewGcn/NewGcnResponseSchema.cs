namespace OCR.Business.NewGcn;

/// <summary>
/// LỚP 2 — Schema đầu ra (định dạng Google AI Studio, type IN HOA) cho màn **OCR GCN iLIS**, bật
/// constrained decoding để model không tự làm hỏng cú pháp JSON. Trước đây chỉ 3 màn Tách GCN có schema
/// (xem <see cref="Split.SplitGcnService"/>), NewGcn thì không — nên hay gặp hai kiểu hỏng:
///  * số có `0` đứng đầu (VD `ma_vach` = `0719220000680`) — JSON không cho phép leading zero;
///  * dấu `"` bị chèn nhầm giữa một dãy số dài (VD `71982"0000232"`).
/// Cả hai đến từ việc model phân vân số/chuỗi ở các trường dạng MÃ. Khai báo những trường đó là
/// `STRING` ở đây khiến model buộc phải xuất chuỗi có ngoặc kép → triệt tiêu gốc, thay vì chỉ vá bằng
/// <see cref="Ai.AiJsonText"/> + retry.
///
/// Chỉ Gemini dùng schema này; nếu provider từ chối (4xx) thì <see cref="Ai.AiModelClient"/> tự gọi lại
/// KHÔNG kèm schema, nên các lớp vá cũ vẫn phải giữ nguyên (không hồi quy).
///
/// ⚠️ Sửa <see cref="Models.NewGcnEnvelope"/> thì phải sửa cả file này — schema lệch model sẽ khiến
/// trường mới không bao giờ được model trả về.
/// </summary>
internal static class NewGcnResponseSchema
{
    /// <summary>Schema bất biến — dựng một lần rồi dùng lại cho mọi file (không mutate sau khi dựng).</summary>
    public static object Instance { get; } = Build();

    /// <summary>Chuỗi — dùng cho mọi trường dạng mã để tránh model xuất number làm hỏng JSON.</summary>
    private static Dictionary<string, object> Str() => new() { ["type"] = "STRING", ["nullable"] = true };

    private static Dictionary<string, object> Int() => new() { ["type"] = "INTEGER", ["nullable"] = true };

    private static Dictionary<string, object> Arr(object item) => new() { ["type"] = "ARRAY", ["items"] = item };

    /// <summary>
    /// ⚠️ <c>propertyOrdering</c> là BẮT BUỘC ở MỌI object, không phải tuỳ chọn làm đẹp: `properties`
    /// là kiểu map trong proto của Google API nên thứ tự khai báo BỊ MẤT khi gửi lên. Thiếu nó, model
    /// sinh key theo thứ tự xáo trộn, lệch hoàn toàn với mẫu JSON trong prompt, và (đo được với
    /// gemini-3.1-flash-lite) bỏ trắng gần hết trường — GCN đọc rõ chủ sử dụng vẫn trả
    /// <c>chu_su_dung_chi_tiet = null</c> mà KHÔNG có lỗi nào, vì object rỗng vẫn hợp lệ với schema.
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

        var owner = Obj(Strings(
            "ho_ten", "gioi_tinh", "nam_sinh", "loai_giay_to", "so_giay_to", "ngay_cap", "noi_cap",
            "so_nha_ngo", "duong_pho", "to_dan_pho", "xa_phuong", "xa_huyen_tinh", "dia_chi_day_du",
            // ma_xa: giấy hầu như không in, nhưng vẫn phải là STRING vì mã xã có số 0 đứng đầu.
            "ma_xa"), required: ["ho_ten"]);

        var mdsd = Obj(Strings(
            "ma_mdsd", "ten_mdsd", "ma_mdsd_quy_hoach", "dien_tich", "ma_htsd", "ten_htsd",
            "thoi_han_su_dung", "ma_ngsd", "ten_ngsd"));

        var rowProperties = Strings(
            "td_so_thua", "td_so_to", "td_tong_dien_tich", "pl_thua_dat",
            "dctd_so_nha_ngo", "dctd_duong_pho", "dctd_to_dan_pho", "dctd_xa_huyen_tinh",
            "dctd_dia_chi_day_du", "ten_don_vi_do", "ngay_hoan_thanh_do",
            "ky_so_vao_so", "ky_ngay_vao_so", "ky_ngay_ky_gcn", "ky_nguoi_ky");
        rowProperties["muc_dich_su_dung"] = Arr(mdsd);
        var row = Obj(rowProperties, required: ["td_so_thua", "td_so_to", "td_tong_dien_tich", "muc_dich_su_dung"]);

        var infoProperties = Strings(
            "so_serial", "loai_mau", "loai_gcn", "loai_quan_he",
            // ma_ho_gia_dinh ("0"/"1") và ma_vach (13 số, hay có 0 đứng đầu) là hai trường gây hỏng JSON
            // nhiều nhất trước khi có schema — bắt buộc STRING.
            "ma_ho_gia_dinh", "ma_vach", "do_tin_cay");
        infoProperties["so_luong_thua_dat_doc_duoc"] = Int();
        infoProperties["thu_tu_trang"] = pageOrder;
        infoProperties["chu_su_dung_chi_tiet"] = Arr(owner);
        infoProperties["thong_tin_thay_doi"] = Arr(new Dictionary<string, object> { ["type"] = "STRING" });
        infoProperties["ghi_chu_trang_1"] = Arr(new Dictionary<string, object> { ["type"] = "STRING" });
        infoProperties["ghi_chu"] = Arr(new Dictionary<string, object> { ["type"] = "STRING" });
        infoProperties["canh_bao"] = Arr(new Dictionary<string, object> { ["type"] = "STRING" });

        // `required` đặt ở những trường BẮT BUỘC PHẢI CÓ MẶT (giá trị vẫn được phép null nhờ
        // `nullable`, nên không ép model bịa): so_luong_gcn_trong_file là tín hiệu duy nhất còn lại
        // để phát hiện PDF ghép nhiều GCN; chu_su_dung_chi_tiet + so_luong_thua_dat_doc_duoc là hai
        // trường mà model từng lặng lẽ bỏ qua, khiến Excel xuất ra không có chủ sử dụng.
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
