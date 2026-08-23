using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeSelectorService _theme;

    // Khớp enum ElementTheme: 0 = Default (Theo hệ thống), 1 = Light (Sáng), 2 = Dark (Tối).
    [ObservableProperty] private int _selectedThemeIndex;

    public SettingsViewModel(IThemeSelectorService theme)
    {
        _theme = theme;
        _selectedThemeIndex = (int)theme.Theme;
    }

    partial void OnSelectedThemeIndexChanged(int value)
        => _theme.SetTheme((ElementTheme)value);
}
