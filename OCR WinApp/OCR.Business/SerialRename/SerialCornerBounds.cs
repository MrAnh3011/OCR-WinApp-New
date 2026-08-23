namespace OCR.Business.SerialRename;

/// <summary>Góc quay thử khi tìm serial. Giá trị = số độ quay THEO CHIỀU KIM ĐỒNG HỒ.</summary>
public enum SerialRotation
{
    None = 0,
    Clockwise90 = 90,
    Clockwise180 = 180,
    Clockwise270 = 270
}

/// <summary>Vùng cắt, tính bằng điểm ảnh, trong hệ toạ độ của ảnh ĐÃ QUAY.</summary>
public readonly record struct SerialCropBounds(uint X, uint Y, uint Width, uint Height);

/// <summary>
/// Tính vùng cắt ứng với GÓC DƯỚI PHẢI của ảnh sau khi quay <see cref="SerialRotation"/> — nơi in số
/// phát hành của GCN. Bản scan có thể bị quay nên phải thử đủ 4 hướng.
///
/// ⚠️ <c>BitmapTransform</c> của WinRT áp <c>Bounds</c> trong hệ toạ độ **SAU KHI QUAY**, không phải toạ
/// độ ảnh gốc — dù tài liệu ghi thứ tự áp là Bounds → Scale → Flip → Rotate. Đo được bằng test
/// end-to-end: tính bounds theo ảnh gốc rồi quay 90° thì <c>GetSoftwareBitmapAsync</c> ném
/// <c>E_INVALIDARG</c> ("Value does not fall within the expected range") vì vùng cắt vượt ra ngoài bề
/// rộng của ảnh đã quay. Vì vậy chỉ cần đổi chỗ hai cạnh khi quay 90°/270° rồi lấy góc dưới phải.
///
/// Thuần số học, không phụ thuộc WinRT nên test trực tiếp được.
/// </summary>
public static class SerialCornerBounds
{
    /// <summary>
    /// Với ảnh gốc <paramref name="width"/>×<paramref name="height"/>, trả về vùng góc dưới phải trong
    /// hệ toạ độ của ảnh sau khi quay <paramref name="rotation"/>.
    /// <paramref name="rightRatio"/> đo theo bề rộng ảnh ĐÃ QUAY, <paramref name="bottomRatio"/> đo theo
    /// chiều cao ảnh ĐÃ QUAY.
    /// </summary>
    public static SerialCropBounds Compute(
        uint width, uint height, SerialRotation rotation, double rightRatio, double bottomRatio)
    {
        if (width == 0 || height == 0) return new SerialCropBounds(0, 0, width, height);

        // Quay 90°/270° thì hai cạnh đổi chỗ: bề rộng ảnh đã quay chính là chiều cao ảnh gốc.
        bool swapped = rotation is SerialRotation.Clockwise90 or SerialRotation.Clockwise270;
        uint rotatedWidth = swapped ? height : width;
        uint rotatedHeight = swapped ? width : height;

        uint spanX = Span(rotatedWidth, Clamp01(rightRatio));
        uint spanY = Span(rotatedHeight, Clamp01(bottomRatio));

        return new SerialCropBounds(rotatedWidth - spanX, rotatedHeight - spanY, spanX, spanY);
    }

    private static uint Span(uint total, double ratio)
    {
        var span = (uint)Math.Round(total * ratio);
        if (span == 0) span = 1;
        return span > total ? total : span;
    }

    private static double Clamp01(double value)
        => double.IsNaN(value) || value <= 0 ? 0.01 : (value > 1 ? 1 : value);
}
