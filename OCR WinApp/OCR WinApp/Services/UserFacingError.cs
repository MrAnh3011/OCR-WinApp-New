using System;

namespace OCR_WinApp.Services;

/// <summary>
/// Chuyển lỗi kỹ thuật thành thông báo an toàn để hiển thị cho người dùng.
/// Log nội bộ vẫn giữ exception gốc ở IErrorLogService.
/// </summary>
public static class UserFacingError
{
    /// <summary>
    /// Câu lỗi mặc định khi một file không xử lý được. Dùng chung cho <see cref="ProcessFile"/> và cho
    /// các chỗ ViewModel tự chốt trạng thái dòng "❌ ..." mà không đi qua exception cụ thể (ví dụ nguồn
    /// hỏng ở phía upload hoặc còn dang dở sau khi luồng xử lý đã kết thúc) — tránh chép lặp chuỗi hiển
    /// thị ở nhiều nơi, dễ lệch câu chữ khi sửa một chỗ mà quên chỗ khác.
    /// </summary>
    public const string ProcessFileFallback = "File này chưa xử lý được. Vui lòng kiểm tra lại file nguồn.";

    private static readonly string[] TechnicalMarkers =
    {
        "ai",
        "llm",
        "json",
        "google",
        "gemini",
        "openrouter",
        "api",
        "http",
        "provider",
        "model",
        "mô hình",
        "mo hinh",
        "schema",
        "response",
        "request",
        "generatecontent",
        "upload",
        "uri",
        "url",
        "x-goog",
        "apikey",
        "api key"
    };

    public static string Prepare(Exception exception)
        => From(exception, "Không thể chuẩn bị dữ liệu xử lý. Vui lòng thử lại.");

    public static string ScanFolder(Exception exception)
        => From(exception, "Không thể quét thư mục. Vui lòng kiểm tra quyền truy cập và thử lại.");

    public static string SplitPdf(Exception exception)
        => From(exception, "Không thể tách PDF. Vui lòng kiểm tra file nguồn và thử lại.");

    public static string ProcessFile(Exception exception)
        => From(exception, ProcessFileFallback);

    public static string Export(Exception exception)
        => From(exception, "Không thể xuất dữ liệu. Vui lòng kiểm tra thư mục lưu và thử lại.");

    public static string Login(string? message)
        => From(message, "Không thể đăng nhập. Vui lòng kiểm tra tài khoản, kết nối hoặc thử lại sau.");

    public static string Login(Exception exception)
        => From(exception, "Không thể đăng nhập. Vui lòng kiểm tra tài khoản, kết nối hoặc thử lại sau.");

    private static string From(Exception exception, string fallback)
        => From(exception.Message, fallback);

    private static string From(string? message, string fallback)
    {
        var safe = Normalize(message);
        if (string.IsNullOrWhiteSpace(safe) || ContainsTechnicalMarker(safe))
            return fallback;

        return safe.Length > 220 ? safe[..220] : safe;
    }

    private static string Normalize(string? message)
        => (message ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

    private static bool ContainsTechnicalMarker(string message)
    {
        var lower = message.ToLowerInvariant();
        foreach (var marker in TechnicalMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
