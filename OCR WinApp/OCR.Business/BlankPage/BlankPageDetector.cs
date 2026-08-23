using OCR.Business.Models;

namespace OCR.Business.BlankPage;

/// <summary>
/// Lõi nhận diện trang trắng — thuần tính toán trên mảng điểm ảnh, KHÔNG đụng tới PDF hay WinRT
/// nên test được trực tiếp.
///
/// Port từ tool Python <c>Installer/BlankPageRemover/remove_blank_pages.py</c>, giữ nguyên 4 bước:
///   1. Ảnh xám của trang (bên gọi lo phần render).
///   2. Cắt bỏ <see cref="BlankPageOptions.CropMarginRatio"/> ở mỗi cạnh — bỏ bóng/vệt mép giấy scan.
///   3. Khử hạt nhiễu lấm tấm bằng bộ lọc MAX 3x3.
///   4. Đếm tỉ lệ điểm ảnh tối hơn <see cref="BlankPageOptions.DarkPixelThreshold"/>.
/// </summary>
public static class BlankPageDetector
{
    /// <summary>
    /// Hệ số chuyển RGB → xám, LẤY ĐÚNG công thức của Pillow <c>Image.convert("L")</c>
    /// (L = R*299/1000 + G*587/1000 + B*114/1000) để kết quả bám sát tool Python.
    /// </summary>
    public static byte ToGray(byte r, byte g, byte b)
        => (byte)((r * 299 + g * 587 + b * 114) / 1000);

    /// <summary>Đổi đệm điểm ảnh BGRA8 (định dạng WinRT trả về) thành mảng xám tuần tự.</summary>
    public static byte[] BgraToGray(byte[] bgra, int width, int height)
    {
        var gray = new byte[width * height];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
            gray[i] = ToGray(bgra[p + 2], bgra[p + 1], bgra[p]);
        return gray;
    }

    /// <summary>Trang có được coi là trắng không.</summary>
    public static bool IsBlank(byte[] gray, int width, int height, BlankPageOptions options)
        => InkRatio(gray, width, height, options) < options.InkRatioThreshold;

    /// <summary>
    /// Tỉ lệ điểm ảnh "có mực" trong vùng đo, sau khi cắt viền và khử nhiễu.
    /// Tách riêng khỏi <see cref="IsBlank"/> để chỉnh ngưỡng có số liệu mà đối chiếu.
    /// </summary>
    public static double InkRatio(byte[] gray, int width, int height, BlankPageOptions options)
    {
        if (width <= 0 || height <= 0 || gray.Length < width * height) return 0;

        int marginX = (int)(width * options.CropMarginRatio);
        int marginY = (int)(height * options.CropMarginRatio);
        int x0 = marginX, x1 = width - marginX;
        int y0 = marginY, y1 = height - marginY;

        // Trang quá nhỏ để cắt viền thì đo nguyên trang, giống nhánh tương ứng của tool Python.
        if (x1 - x0 <= 10 || y1 - y0 <= 10)
        {
            x0 = 0; y0 = 0; x1 = width; y1 = height;
        }

        long total = (long)(x1 - x0) * (y1 - y0);
        if (total <= 0) return 0;

        int threshold = options.DarkPixelThreshold;
        // Vượt mốc này là chắc chắn KHÔNG trắng — thoát sớm, khỏi quét nốt trang đầy chữ.
        double limit = total * options.InkRatioThreshold;
        long dark = 0;

        for (int y = y0; y < y1; y++)
        {
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                // Bộ lọc MAX chỉ làm SÁNG lên, nên điểm đã sáng sẵn thì sau lọc vẫn sáng: bỏ qua ngay.
                // Nhờ vậy chỉ những điểm tối thật mới phải tính max 3x3 — trang trắng gần như không tốn gì.
                if (gray[row + x] >= threshold) continue;

                // Sau lọc MAX 3x3, điểm này còn tối ⟺ CẢ 9 điểm lân cận đều tối.
                // Điểm tối đơn lẻ (hạt nhiễu scan) sẽ bị hàng xóm sáng kéo lên và không được tính.
                if (Max3x3(gray, width, height, x, y) >= threshold) continue;

                if (++dark >= limit) return (double)dark / total;
            }
        }

        return (double)dark / total;
    }

    /// <summary>Giá trị lớn nhất trong ô 3x3 quanh (x, y); ra ngoài biên thì lặp lại điểm biên như Pillow.</summary>
    private static byte Max3x3(byte[] gray, int width, int height, int x, int y)
    {
        byte max = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int yy = Math.Clamp(y + dy, 0, height - 1) * width;
            for (int dx = -1; dx <= 1; dx++)
            {
                byte value = gray[yy + Math.Clamp(x + dx, 0, width - 1)];
                if (value > max) max = value;
            }
        }
        return max;
    }
}
