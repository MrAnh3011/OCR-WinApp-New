using Microsoft.UI.Xaml;

namespace OCR_WinApp.Services;

/// <summary>Lưu và áp dụng theme Sáng/Tối/Theo hệ thống.</summary>
public interface IThemeSelectorService
{
    ElementTheme Theme { get; }
    void Initialize();
    void SetTheme(ElementTheme theme);
}
