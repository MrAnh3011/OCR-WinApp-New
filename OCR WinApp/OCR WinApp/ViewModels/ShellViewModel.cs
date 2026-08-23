using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using OCR.Business.Auth;
using OCR_WinApp.Views;

namespace OCR_WinApp.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    [ObservableProperty]
    private string? _currentUser;

    public string CurrentUserInitial => GetInitial(CurrentUser);
    public string GreetingText => $"Xin chào, {CurrentUser}";

    public ShellViewModel(IAuthService auth)
    {
        _auth = auth;
        _currentUser = auth.CurrentUser;
    }

    private static string GetInitial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        return name.Trim()[0].ToString().ToUpperInvariant();
    }

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
        if (App.MainWindow?.Content is Frame rootFrame)
            rootFrame.Navigate(typeof(LoginPage));
    }
}
