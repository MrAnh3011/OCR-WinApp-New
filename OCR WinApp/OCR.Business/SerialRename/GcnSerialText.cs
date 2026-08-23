using System.Text.RegularExpressions;

namespace OCR.Business.SerialRename;

/// <summary>
/// Tách số serial GCN ra khỏi chuỗi text do OCR trả về. Thuần xử lý chuỗi nên test trực tiếp được.
/// </summary>
public static class GcnSerialText
{
    /// <summary>
    /// Serial in trên GCN: 1–2 chữ cái + 6 số (mẫu cũ) hoặc 8 số (mẫu QR), giữa có thể có khoảng trắng.
    ///
    /// ⚠️ KHÁC regex phục hồi serial từ TÊN FILE trong <c>NewGcnExtractService.TryExtractSerial</c>:
    /// bản đó BẮT BUỘC có dấu phân cách (<c>[\s_-]+</c>) để không khớp bừa vào các dãy số khác trong tên
    /// file. Ở đây đọc từ ảnh giấy nên phải nhận cả dạng liền (<c>AA123456</c>) — dạng in phổ biến nhất.
    ///
    /// Cố ý KHÔNG tự sửa các nhầm lẫn quen thuộc của OCR (O↔0, I↔1, S↔5): đọc sai một ký tự là đặt sai
    /// tên thư mục hồ sơ, tai hại hơn nhiều so với việc báo "không đọc được" để người dùng xử lý tay.
    /// </summary>
    private static readonly Regex Pattern = new(
        @"(?<![A-Za-z0-9])([A-Za-z]{1,2})[\s.·_-]{0,3}(\d{6}|\d{8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Trả về serial đã chuẩn hoá dạng <c>"AA 123456"</c> (chữ in hoa, đúng một khoảng trắng),
    /// hoặc chuỗi rỗng nếu không tìm thấy.
    /// </summary>
    public static string TryExtract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var match = Pattern.Match(text);
        if (!match.Success) return "";

        return $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}";
    }

    /// <summary>Bỏ khoảng trắng và ký tự không hợp lệ để dùng làm tên file/thư mục.</summary>
    public static string ToFolderName(string? serial)
    {
        var normalized = string.Join(" ", (serial ?? "")
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return "";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '_');

        // Windows cắt bỏ dấu cách / dấu chấm ở cuối tên thư mục — tự trim để tên tạo ra đúng như hiển thị.
        return normalized.Trim().TrimEnd('.');
    }
}
