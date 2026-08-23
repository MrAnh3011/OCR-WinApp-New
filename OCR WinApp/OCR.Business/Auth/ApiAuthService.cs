using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OCR.Business.Models;

namespace OCR.Business.Auth;

/// <summary>Xac thuc qua backend API va kiem tra han muc OCR sau khi dang nhap.</summary>
public sealed class ApiAuthService : IAuthService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public bool IsAuthenticated { get; private set; }
    public string? CurrentUser { get; private set; }
    public AuthSession? CurrentSession { get; private set; }

    public ApiAuthService(AuthApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("BaseUrl của API đăng nhập chưa được cấu hình.", nameof(options));

        _http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30)
        };
    }

    public async Task<Result> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        username = username.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Result.Fail("Vui lòng nhập tên đăng nhập và mật khẩu.");

        try
        {
            var login = await LoginCoreAsync(username, password, ct);
            if (!login.IsSuccess || login.Value is null)
                return Result.Fail(login.Error ?? "Sai tài khoản hoặc mật khẩu.");

            var session = new AuthSession
            {
                AccessToken = login.Value.AccessToken,
                UserId = login.Value.UserId,
                UserName = login.Value.UserName,
                FullName = login.Value.FullName
            };

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);

            var quota = await GetQuotaAsync(session.UserId, ct);
            if (!quota.IsSuccess || quota.Value is null)
                return Result.Fail(quota.Error ?? "Không lấy được thông tin hạn mức. Vui lòng thử lại.");

            if (!quota.Value.IsActive)
                return Result.Fail("Tài khoản bị khóa. Liên hệ quản trị viên để mở lại.");

            if (quota.Value.ConLai <= 0)
                return Result.Fail($"Đã hết hạn mức OCR ({quota.Value.SoFileDaDoc}/{quota.Value.HanMuc} file). Liên hệ quản trị viên.");

            CurrentSession = new AuthSession
            {
                AccessToken = session.AccessToken,
                UserId = session.UserId,
                UserName = session.UserName,
                FullName = session.FullName,
                Quota = quota.Value
            };
            IsAuthenticated = true;
            CurrentUser = CurrentSession.DisplayName;
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Fail("Đã hủy đăng nhập.");
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail($"Lỗi kết nối API đăng nhập: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return Result.Fail("Kết nối API đăng nhập quá thời gian chờ.");
        }
        catch (Exception ex)
        {
            return Result.Fail($"Lỗi đăng nhập: {ex.Message}");
        }
    }

    public void Logout()
    {
        IsAuthenticated = false;
        CurrentUser = null;
        CurrentSession = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<Result> RecordOcrCreditAsync(
        string tenFile,
        string duongDanFile,
        int soTrang,
        CancellationToken ct = default)
    {
        if (soTrang <= 0) return Result.Success();
        if (CurrentSession is null || string.IsNullOrWhiteSpace(CurrentSession.UserId))
            return Result.Fail("Phiên đăng nhập không hợp lệ.");

        try
        {
            using var res = await _http.PostAsJsonAsync(
                "Sys_UserOcrCredit/GhiNhanFileDoc",
                new
                {
                    userId = CurrentSession.UserId,
                    tenFile = tenFile ?? "",
                    duongDanFile = duongDanFile ?? "",
                    soTrang
                },
                JsonOptions,
                ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return Result.Fail($"Không ghi nhận được hạn mức xử lý HTTP {(int)res.StatusCode}: {body}");

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var wrapper = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(body, JsonOptions);
                    if (wrapper?.Success == false)
                        return Result.Fail(string.IsNullOrWhiteSpace(wrapper.Message)
                            ? "Không ghi nhận được hạn mức xử lý."
                            : wrapper.Message);
                }
                catch (JsonException)
                {
                    // Endpoint co the tra ve body khac ApiResponse; HTTP 2xx duoc xem la thanh cong.
                }
            }

            if (CurrentSession.Quota is not null)
            {
                CurrentSession.Quota.SoFileDaDoc += soTrang;
                CurrentSession.Quota.SoFileConLai = Math.Max(0, CurrentSession.Quota.ConLai - soTrang);
            }

            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Fail("Đã hủy ghi nhận hạn mức xử lý.");
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail("Không kết nối được máy chủ ghi nhận hạn mức: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return Result.Fail("Kết nối máy chủ ghi nhận hạn mức quá thời gian chờ.");
        }
        catch (Exception ex)
        {
            return Result.Fail("Không ghi nhận được hạn mức xử lý: " + ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<Result<LoginApiResult>> LoginCoreAsync(string username, string password, CancellationToken ct)
    {
        using var res = await _http.PostAsJsonAsync(
            "Sys_Account/Login",
            new LoginRequest { UserName = username, Password = password },
            JsonOptions,
            ct);

        if (!res.IsSuccessStatusCode)
            return Result<LoginApiResult>.Fail("Sai tài khoản hoặc mật khẩu.");

        var wrapper = await res.Content.ReadFromJsonAsync<ApiResponse<LoginApiResult>>(JsonOptions, ct);
        if (wrapper?.Success != true || wrapper.Data is null)
            return Result<LoginApiResult>.Fail(string.IsNullOrWhiteSpace(wrapper?.Message)
                ? "Sai tài khoản hoặc mật khẩu."
                : wrapper.Message);

        if (string.IsNullOrWhiteSpace(wrapper.Data.AccessToken) || string.IsNullOrWhiteSpace(wrapper.Data.UserId))
            return Result<LoginApiResult>.Fail("Phản hồi đăng nhập không hợp lệ.");

        return Result<LoginApiResult>.Success(wrapper.Data);
    }

    private async Task<Result<OcrQuotaInfo>> GetQuotaAsync(string userId, CancellationToken ct)
    {
        var wrapper = await _http.GetFromJsonAsync<ApiResponse<OcrQuotaInfo>>(
            $"Sys_UserOcrCredit/{Uri.EscapeDataString(userId)}",
            JsonOptions,
            ct);

        return wrapper?.Success == true && wrapper.Data is not null
            ? Result<OcrQuotaInfo>.Success(wrapper.Data)
            : Result<OcrQuotaInfo>.Fail(string.IsNullOrWhiteSpace(wrapper?.Message)
                ? "Không lấy được thông tin hạn mức. Vui lòng thử lại."
                : wrapper.Message);
    }

    private sealed class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }
    }

    private sealed class LoginRequest
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private sealed class LoginApiResult
    {
        public string AccessToken { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
    }
}
