using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OCR.Business.Auth;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IBrandService _brand;
    private readonly ILoginPreferencesService _prefs;
    private readonly IErrorLogService _errorLog;

    /// <summary>Phát khi đăng nhập thành công để View điều hướng sang Shell.</summary>
    public event EventHandler? LoginSucceeded;

    // Thông tin thương hiệu hiển thị ở cột phải (theo flavor).
    public string BrandName => _brand.DisplayName;
    public string Slogan => _brand.Slogan;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public LoginViewModel(IAuthService auth, IBrandService brand, ILoginPreferencesService prefs, IErrorLogService errorLog)
    {
        _auth = auth;
        _brand = brand;
        _prefs = prefs;
        _errorLog = errorLog;

        // Nạp tùy chọn ghi nhớ: tự điền lại tên đăng nhập nếu trước đó đã chọn.
        RememberMe = _prefs.RememberMe;
        if (_prefs.RememberMe)
            Username = _prefs.SavedUsername;
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private bool CanLogin() => !IsBusy
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _auth.LoginAsync(Username, Password);
            if (result.IsSuccess)
            {
                _prefs.Save(RememberMe, Username);
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
                ErrorMessage = UserFacingError.Login(result.Error);
        }
        catch (Exception ex)
        {
            _errorLog.LogException("login", "LoginAsync", ex);
            ErrorMessage = UserFacingError.Login(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
