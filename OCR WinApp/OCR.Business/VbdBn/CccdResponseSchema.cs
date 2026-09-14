using System.Collections.Generic;
using System.Linq;

namespace OCR.Business.VbdBn;

/// <summary>
/// Schema đầu ra (định dạng Google AI Studio, type IN HOA) cho luồng CCCD của màn VBD-BN.
/// `propertyOrdering` BẮT BUỘC ở mọi object — cùng lý do đã ghi ở <see cref="NewGcn.NewGcnResponseSchema"/>.
/// ⚠️ Sửa <see cref="CccdRecord"/> thì phải sửa cả file này.
/// </summary>
internal static class CccdResponseSchema
{
    public static object Instance { get; } = Build();

    private static Dictionary<string, object> Str() => new() { ["type"] = "STRING", ["nullable"] = true };

    private static Dictionary<string, object> IntArr()
        => new() { ["type"] = "ARRAY", ["items"] = new Dictionary<string, object> { ["type"] = "INTEGER" } };

    private static Dictionary<string, object> StrArr()
        => new() { ["type"] = "ARRAY", ["items"] = new Dictionary<string, object> { ["type"] = "STRING" } };

    private static Dictionary<string, object> Obj(Dictionary<string, object> properties, string[]? required = null)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "OBJECT",
            ["properties"] = properties,
            ["propertyOrdering"] = properties.Keys.ToArray()
        };
        if (required is { Length: > 0 }) schema["required"] = required;
        return schema;
    }

    private static object Build()
    {
        var record = Obj(new Dictionary<string, object>
        {
            ["loai_giay_to"] = Str(),
            ["so_giay_to"] = Str(),
            ["ho_ten"] = Str(),
            ["ngay_sinh"] = Str(),
            ["gioi_tinh"] = Str(),
            ["quoc_tich"] = Str(),
            ["que_quan"] = Str(),
            ["noi_thuong_tru"] = Str(),
            ["ngay_cap"] = Str(),
            ["noi_cap"] = Str(),
            ["co_gia_tri_den"] = Str(),
            ["trang"] = IntArr(),
            ["do_tin_cay"] = Str(),
            ["canh_bao"] = StrArr()
        }, required: new[] { "loai_giay_to", "so_giay_to", "trang" });

        return Obj(new Dictionary<string, object>
        {
            ["danh_sach_giay_to"] = new Dictionary<string, object> { ["type"] = "ARRAY", ["items"] = record }
        }, required: new[] { "danh_sach_giay_to" });
    }
}
