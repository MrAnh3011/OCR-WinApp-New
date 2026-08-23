using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using OCR_WinApp.Helpers;

namespace OCR_WinApp.Services;

public sealed class ThemeSelectorService : IThemeSelectorService
{
    // Lưu vào file (chạy được cả packaged lẫn unpackaged — ApplicationData.Current không có ở unpackaged).
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "settings.json");

    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    public void Initialize()
    {
        if (Enum.TryParse<ElementTheme>(ReadStored(), out var theme))
            Theme = theme;
        Apply();
    }

    public void SetTheme(ElementTheme theme)
    {
        Theme = theme;
        WriteStored(theme.ToString());
        Apply();
    }

    private void Apply()
    {
        if (App.MainWindow is null) return;

        if (App.MainWindow.Content is FrameworkElement root)
            root.RequestedTheme = Theme;

        // Áp lại màu 3 nút caption cho khớp theme.
        TitleBarHelper.ApplyCaptionButtonColors(App.MainWindow, Theme);
    }

    private static string? ReadStored()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("AppTheme", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    private static void WriteStored(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { AppTheme = theme }));
        }
        catch { /* bỏ qua lỗi ghi cài đặt */ }
    }
}
