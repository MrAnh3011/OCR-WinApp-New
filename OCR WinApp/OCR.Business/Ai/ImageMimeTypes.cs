namespace OCR.Business.Ai;

/// <summary>
/// Bảng ánh xạ đuôi file ảnh ↔ MIME type dùng CHUNG cho mọi pipeline gửi ảnh lên AI provider
/// (GCN New, ba màn Tách, Đất Uỷ Ban). Tập trung một chỗ để tránh mỗi màn tự đoán MIME một kiểu —
/// lệch nhau sẽ khiến ảnh (đặc biệt TIFF) bị gắn sai nhãn khi upload lên Gemini Files API.
/// </summary>
public static class ImageMimeTypes
{
    /// <summary>Đoán MIME type ảnh từ đuôi file nguồn (không phân biệt hoa/thường). Đuôi lạ (ví dụ .pdf khi đã render ra JPEG) mặc định "image/jpeg".</summary>
    public static string FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _ => "image/jpeg"
    };

    /// <summary>Đuôi file tương ứng một MIME type ảnh — dùng đặt tên hiển thị cho artifact upload.</summary>
    public static string ToExtension(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tiff",
        _ => ".jpg"
    };
}
