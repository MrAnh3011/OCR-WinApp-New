using System;
using Microsoft.UI.Xaml.Controls;

namespace OCR_WinApp.Navigation;

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame) => _frame = frame;

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame is null) return false;
        if (_frame.CurrentSourcePageType == pageType && parameter is null) return false;
        return _frame.Navigate(pageType, parameter);
    }
}
