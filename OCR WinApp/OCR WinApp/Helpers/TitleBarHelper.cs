using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace OCR_WinApp.Helpers;

/// <summary>
/// Đổi màu 3 nút caption (minimize/maximize/close) theo theme app.
/// Chỉ có tác dụng khi Window.ExtendsContentIntoTitleBar = true.
/// </summary>
public static class TitleBarHelper
{
    public static void ApplyCaptionButtonColors(Window window, ElementTheme requestedTheme)
    {
        var titleBar = window.AppWindow?.TitleBar;
        if (titleBar is null) return;

        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        var isDark = ResolveIsDark(window, requestedTheme);
        var foreground = isDark ? Colors.White : Colors.Black;
        var inactive = isDark ? Color.FromArgb(255, 0x99, 0x99, 0x99) : Color.FromArgb(255, 0x77, 0x77, 0x77);

        // Nền nút trong suốt để hòa với Mica / vùng title bar.
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;

        // Nền hover/pressed mờ, đậm/nhạt theo theme.
        titleBar.ButtonHoverBackgroundColor = isDark
            ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x17, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedBackgroundColor = isDark
            ? Color.FromArgb(0x53, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x2E, 0x00, 0x00, 0x00);
    }

    private static bool ResolveIsDark(Window window, ElementTheme requestedTheme)
    {
        if (requestedTheme == ElementTheme.Dark) return true;
        if (requestedTheme == ElementTheme.Light) return false;

        // Theo hệ thống: lấy theme thực tế của nội dung nếu có, ngược lại theo app.
        if (window.Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default)
            return root.ActualTheme == ElementTheme.Dark;

        return Application.Current.RequestedTheme == ApplicationTheme.Dark;
    }
}
