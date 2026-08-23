using System;
using Windows.UI;

namespace OCR_WinApp.Services;

/// <summary>
/// Đọc flavor (tông màu/logo/slogan) từ appsettings.json. Một tham số <c>Flavor</c>
/// quyết định toàn bộ giao diện thương hiệu của app.
/// </summary>
public interface IBrandService
{
    /// <summary>Nạp cấu hình + chọn flavor. Gọi đầu OnLaunched (trước khi tạo cửa sổ).</summary>
    void Initialize();

    string FlavorKey { get; }
    string DisplayName { get; }
    string Slogan { get; }

    /// <summary>Màu tint áp vào nền blur của app.</summary>
    Color TintColor { get; }

    /// <summary>Độ đậm tint của nền blur (0..1).</summary>
    double TintOpacity { get; }

    /// <summary>Độ mờ-sương của nền blur (0..1).</summary>
    double LuminosityOpacity { get; }

    /// <summary>Màu chữ toàn app; null = dùng mặc định theo theme.</summary>
    Color? TextColor { get; }

    /// <summary>URI banner đăng nhập theo flavor: ms-appx:///Assets/{folder}/banner.png</summary>
    Uri BannerUri { get; }

    /// <summary>Đường dẫn file icon.ico (trên đĩa) theo flavor — dùng cho icon cửa sổ/taskbar.</summary>
    string IconIcoPath { get; }
}
