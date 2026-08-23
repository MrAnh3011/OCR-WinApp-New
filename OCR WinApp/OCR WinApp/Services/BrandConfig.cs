using System.Collections.Generic;

namespace OCR_WinApp.Services;

/// <summary>Gốc của appsettings.json (phần Appearance).</summary>
public sealed class AppSettingsRoot
{
    public AppearanceConfig Appearance { get; set; } = new();
}

/// <summary>
/// Cấu hình thương hiệu. <see cref="Flavor"/> là THAM SỐ DUY NHẤT chọn tông màu nền,
/// thư mục assets (logo) và slogan — đặt thủ công trước khi build từng app.
/// </summary>
public sealed class AppearanceConfig
{
    public string Flavor { get; set; } = "Green";
    public Dictionary<string, BrandFlavor> Flavors { get; set; } = new();
}

/// <summary>Một "phiên bản thương hiệu": màu nền blur + thư mục logo + slogan.</summary>
public sealed class BrandFlavor
{
    public string DisplayName { get; set; } = "OCR WinApp";

    /// <summary>Màu tint áp vào nền blur (acrylic) của app, dạng #RRGGBB.</summary>
    public string TintColor { get; set; } = "#18A957";

    /// <summary>Độ đậm của lớp tint trên nền blur (0..1). Cao = đặc màu, thấp = trong.</summary>
    public double TintOpacity { get; set; } = 0.45;

    /// <summary>Độ "mờ-sương" của nền blur (0..1).</summary>
    public double LuminosityOpacity { get; set; } = 0.9;

    /// <summary>Màu chữ toàn app (#RRGGBB). Rỗng = tự động theo theme.</summary>
    public string TextColor { get; set; } = "";

    /// <summary>Tên thư mục con trong Assets chứa logo/media của flavor này.</summary>
    public string AssetsFolder { get; set; } = "Green";

    public string Slogan { get; set; } = "";
}

/// <summary>Cấu hình mặc định nếu không đọc được appsettings.json.</summary>
internal static class BrandDefaults
{
    public static AppearanceConfig Create() => new()
    {
        Flavor = "Green",
        Flavors = new Dictionary<string, BrandFlavor>
        {
            ["Green"] = new BrandFlavor
            {
                DisplayName = "OCR WinApp", TintColor = "#18A957",
                AssetsFolder = "Green", Slogan = "Số hóa tài liệu nhanh và chính xác"
            },
            ["Sapphire"] = new BrandFlavor
            {
                DisplayName = "OCR WinApp", TintColor = "#0F52BA",
                AssetsFolder = "Sapphire", Slogan = "Giải pháp OCR thông minh cho doanh nghiệp"
            }
        }
    };
}
