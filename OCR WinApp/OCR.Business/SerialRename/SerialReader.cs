using OCR.Business.Models;
using OCR.Business.Ocr;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OCR.Business.SerialRename;

/// <summary>
/// Đọc số serial ở góc dưới phải TRANG 1 của GCN bằng OCR offline (<see cref="IOcrEngine"/> →
/// <c>Windows.Media.Ocr</c>). Không gọi API, không tốn hạn mức.
///
/// Thứ tự thử, dừng ngay khi ra chuỗi khớp mẫu serial:
///   1. Vùng góc dưới phải, lần lượt 4 hướng quay 0° / 90° / 180° / 270°
///      (bản scan bị quay thì "góc dưới phải" của tờ giấy nằm ở góc khác của ảnh — xem
///      <see cref="SerialCornerBounds"/>).
///   2. Nếu vẫn không ra: OCR CẢ trang 1, cũng đủ 4 hướng.
/// Bước 2 để sau vì đọc cả trang dễ khớp nhầm vào dãy số khác trên giấy (số vào sổ, mã vạch…);
/// vùng góc cho kết quả chính xác hơn nên phải được ưu tiên.
/// </summary>
public sealed class SerialReader : ISerialReader
{
    private static readonly SerialRotation[] Rotations =
    [
        SerialRotation.None,
        SerialRotation.Clockwise90,
        SerialRotation.Clockwise180,
        SerialRotation.Clockwise270
    ];

    /// <summary>
    /// Cạnh dài tối đa khi OCR CẢ TRANG ở nhánh dự phòng. Vùng góc chỉ chiếm ~11% diện tích nên giải mã
    /// nguyên trang mới là phần tốn bộ nhớ: A4 ở 300 DPI là 2480×3508, dạng BGRA8 tốn ~35 MB mỗi lần, mà
    /// nhánh dự phòng phải thử 4 hướng → WinRT ném E_OUTOFMEMORY (đã gặp thật khi test trang quay 180°).
    /// Thu nhỏ về mức này vẫn dư sức đọc số phát hành mà giảm bộ nhớ khoảng 3 lần.
    /// </summary>
    private const uint MaxFullPageSide = 2000;

    private readonly IOcrEngine _ocr;
    private readonly SerialRenameOptions _options;

    public SerialReader(IOcrEngine ocr, SerialRenameOptions options)
    {
        _ocr = ocr;
        _options = options;
    }

    public async Task<SerialScan> ReadAsync(string pdfPath, string relativePath, CancellationToken ct = default)
    {
        PdfDocument document;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            document = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex)
        {
            return Failed(pdfPath, relativePath, SerialReadStatus.OpenError, Describe(ex));
        }

        if (document.PageCount == 0)
            return Failed(pdfPath, relativePath, SerialReadStatus.OpenError, "PDF khong co trang nao");

        try
        {
            using var rendered = new InMemoryRandomAccessStream();
            using (var page = document.GetPage(0))
            {
                var size = page.Size;
                // PdfPage.Size theo DIP (96 DIP = 1 inch) nên hệ số này cho ra đúng DPI đã cấu hình.
                double scale = Math.Max(0.1, _options.RenderDpi / 96.0);
                await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, size.Width * scale),
                    DestinationHeight = (uint)Math.Max(1, size.Height * scale)
                });
            }

            rendered.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(rendered);

            // Lượt 1: vùng góc dưới phải theo từng hướng quay.
            foreach (var rotation in Rotations)
            {
                ct.ThrowIfCancellationRequested();
                var bounds = SerialCornerBounds.Compute(
                    decoder.PixelWidth, decoder.PixelHeight, rotation,
                    _options.CropRightRatio, _options.CropBottomRatio);

                var serial = await RecognizeSerialAsync(decoder, rotation, bounds, ct);
                if (serial.Length > 0) return Read(pdfPath, relativePath, serial, $"vung goc, quay {(int)rotation} do");
            }

            // Lượt 2: cả trang, vẫn đủ 4 hướng.
            foreach (var rotation in Rotations)
            {
                ct.ThrowIfCancellationRequested();
                var serial = await RecognizeSerialAsync(decoder, rotation, bounds: null, ct);
                if (serial.Length > 0) return Read(pdfPath, relativePath, serial, $"ca trang, quay {(int)rotation} do");
            }

            return Failed(pdfPath, relativePath, SerialReadStatus.NotFound,
                "Khong tim thay chuoi khop mau serial o trang 1");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(pdfPath, relativePath, SerialReadStatus.OpenError, Describe(ex));
        }
    }

    private async Task<string> RecognizeSerialAsync(
        BitmapDecoder decoder, SerialRotation rotation, SerialCropBounds? bounds, CancellationToken ct)
    {
        var transform = new BitmapTransform { Rotation = ToBitmapRotation(rotation) };

        if (bounds is { } crop)
        {
            // Bounds tính trong hệ toạ độ ảnh ĐÃ QUAY — xem chú thích của SerialCornerBounds.
            transform.Bounds = new BitmapBounds
            {
                X = crop.X, Y = crop.Y, Width = crop.Width, Height = crop.Height
            };
        }
        else
        {
            // Nhánh dự phòng đọc cả trang: thu nhỏ để không ngốn bộ nhớ (xem MaxFullPageSide).
            bool swapped = rotation is SerialRotation.Clockwise90 or SerialRotation.Clockwise270;
            uint rotatedWidth = swapped ? decoder.PixelHeight : decoder.PixelWidth;
            uint rotatedHeight = swapped ? decoder.PixelWidth : decoder.PixelHeight;
            uint longSide = Math.Max(rotatedWidth, rotatedHeight);

            if (longSide > MaxFullPageSide)
            {
                double factor = (double)MaxFullPageSide / longSide;
                transform.ScaledWidth = Math.Max(1, (uint)(rotatedWidth * factor));
                transform.ScaledHeight = Math.Max(1, (uint)(rotatedHeight * factor));
            }
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var page = await _ocr.RecognizeAsync(bitmap, pageNumber: 1, _options.OcrLanguage, ct);

        // Dò theo TỪNG DÒNG trước: serial in trên một dòng riêng, tìm trong dòng đó thì không thể khớp
        // vắt qua hai dòng khác nhau (VD số cuối của dòng trên + số đầu của dòng dưới).
        foreach (var line in page.Lines)
        {
            var fromLine = GcnSerialText.TryExtract(line.Text);
            if (fromLine.Length > 0) return fromLine;
        }

        // Chỉ khi từng dòng đều không ra mới thử cả khối text: OCR đôi lúc gộp/tách dòng khác dự đoán.
        return GcnSerialText.TryExtract(page.FullText);
    }

    private static BitmapRotation ToBitmapRotation(SerialRotation rotation) => rotation switch
    {
        SerialRotation.Clockwise90 => BitmapRotation.Clockwise90Degrees,
        SerialRotation.Clockwise180 => BitmapRotation.Clockwise180Degrees,
        SerialRotation.Clockwise270 => BitmapRotation.Clockwise270Degrees,
        _ => BitmapRotation.None
    };

    /// <summary>
    /// Exception của WinRT khi mở PDF hỏng nhiều lúc có <c>Message</c> RỖNG (HRESULT không map được sang
    /// chuỗi) — lúc đó phải rơi về tên kiểu + mã HRESULT, nếu không log sẽ không nói được lý do gì.
    /// </summary>
    private static string Describe(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message)
            ? $"{ex.GetType().Name} (HRESULT 0x{ex.HResult:X8})"
            : ex.Message.Trim();

    private static SerialScan Read(string pdfPath, string relativePath, string serial, string note) => new()
    {
        SourcePath = pdfPath,
        RelativePath = relativePath,
        Status = SerialReadStatus.Read,
        Serial = serial,
        Note = note
    };

    private static SerialScan Failed(
        string pdfPath, string relativePath, SerialReadStatus status, string note) => new()
    {
        SourcePath = pdfPath,
        RelativePath = relativePath,
        Status = status,
        Note = note
    };
}
