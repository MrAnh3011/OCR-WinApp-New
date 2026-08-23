using System.Threading.Tasks;

namespace OCR_WinApp.Services;

/// <summary>
/// Hỏi mã xã áp cho cả lô ở màn OCR GCN VietBD. Giấy chứng nhận không in mã xã của thửa đất nên
/// cột `DDK_maXa` (B) của khuôn Việt Bản Đồ không thể lấy từ OCR — người dùng nhập một lần cho cả lô.
/// </summary>
public interface IMaXaPromptService
{
    /// <summary>
    /// Trả về mã xã đã nhập (đã trim), hoặc <c>null</c> khi người dùng huỷ / bỏ trống.
    /// <paramref name="defaultValue"/> dùng để điền sẵn mã xã của lần chạy trước.
    /// </summary>
    Task<string?> AskAsync(string? defaultValue = null);
}
