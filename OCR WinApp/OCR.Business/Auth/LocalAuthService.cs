using OCR.Business.Models;

namespace OCR.Business.Auth;

/// <summary>
/// Mock cuc bo dung khi chua cau hinh API. Chap nhan tai khoan/mat khau khong rong.
/// </summary>
public sealed class LocalAuthService : IAuthService
{
    public bool IsAuthenticated { get; private set; }
    public string? CurrentUser { get; private set; }
    public AuthSession? CurrentSession { get; private set; }

    public async Task<Result> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Result.Fail("Vui lòng nhập tên đăng nhập và mật khẩu.");

        CurrentSession = new AuthSession
        {
            UserId = username,
            UserName = username,
            FullName = username
        };
        IsAuthenticated = true;
        CurrentUser = CurrentSession.DisplayName;
        return Result.Success();
    }

    public Task<Result> RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public void Logout()
    {
        IsAuthenticated = false;
        CurrentUser = null;
        CurrentSession = null;
    }
}
