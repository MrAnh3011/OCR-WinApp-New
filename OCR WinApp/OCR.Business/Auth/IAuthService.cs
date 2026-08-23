using OCR.Business.Models;

namespace OCR.Business.Auth;

/// <summary>Hop dong xac thuc nguoi dung cho tang App.</summary>
public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? CurrentUser { get; }
    AuthSession? CurrentSession { get; }

    Task<Result> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<Result> RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang, CancellationToken ct = default);
    void Logout();
}
