using System;
using Microsoft.UI.Xaml.Controls;

namespace OCR_WinApp.Navigation;

/// <summary>Điều hướng nội dung bên trong Shell (ContentFrame của NavigationView).</summary>
public interface INavigationService
{
    void Initialize(Frame frame);
    bool NavigateTo(Type pageType, object? parameter = null);
}
