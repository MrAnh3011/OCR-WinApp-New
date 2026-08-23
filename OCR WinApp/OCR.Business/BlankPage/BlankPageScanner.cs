using OCR.Business.Models;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OCR.Business.BlankPage;

/// <summary>
/// Render từng trang PDF bằng Windows.Data.Pdf rồi giao cho <see cref="BlankPageDetector"/> phán định.
///
/// KHÔNG dùng lại <c>IPdfRenderer</c> vì hai lý do:
///   - <c>RenderPagesAsync</c> render TOÀN BỘ trang vào bộ nhớ một lượt; màn này quét cả cây thư mục
///     nên phải xử lý từng trang rồi giải phóng ngay, tránh phình bộ nhớ với PDF nhiều trang.
///   - Ở đây cần đúng độ phân giải theo <see cref="BlankPageOptions.RenderDpi"/>, không phải hệ số
///     phóng to cố định 2.0 vốn dành cho OCR.
/// </summary>
public sealed class BlankPageScanner : IBlankPageScanner
{
    private readonly BlankPageOptions _options;

    public BlankPageScanner(BlankPageOptions options) => _options = options;

    public async Task<BlankPageScan> ScanAsync(string pdfPath, string relativePath, CancellationToken ct = default)
    {
        PdfDocument document;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            document = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex)
        {
            // PDF hỏng, đang bị khoá, hoặc có mật khẩu mở file. Export sẽ copy nguyên bản gốc.
            return Failed(pdfPath, relativePath, Describe(ex));
        }

        int pageCount = (int)document.PageCount;
        if (pageCount == 0)
            return Failed(pdfPath, relativePath, "PDF khong co trang nao");

        var blanks = new List<int>();
        // PdfPage.Size tính theo DIP (96 DIP = 1 inch) nên hệ số này cho ra đúng số điểm ảnh mà
        // tool Python đạt được ở cùng mức DPI (pypdfium2 dùng point, 72 point = 1 inch).
        double scale = _options.RenderDpi / 96.0;

        for (int i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            using var page = document.GetPage((uint)i);
            using var stream = new InMemoryRandomAccessStream();

            var size = page.Size;
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, size.Width * scale),
                DestinationHeight = (uint)Math.Max(1, size.Height * scale)
            });
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            int width = (int)decoder.PixelWidth;
            int height = (int)decoder.PixelHeight;
            var gray = BlankPageDetector.BgraToGray(pixels.DetachPixelData(), width, height);

            if (BlankPageDetector.IsBlank(gray, width, height, _options)) blanks.Add(i);
        }

        if (blanks.Count == 0)
        {
            return new BlankPageScan
            {
                SourcePath = pdfPath,
                RelativePath = relativePath,
                Status = BlankPageStatus.NoBlank,
                TotalPages = pageCount
            };
        }

        // Trắng hết thì GIỮ NGUYÊN file gốc và cảnh báo: xoá sạch trang là mất dữ liệu, mà nguyên nhân
        // thường là ngưỡng đặt sai hoặc bản scan hỏng chứ không phải giấy trắng thật.
        if (blanks.Count == pageCount)
        {
            return new BlankPageScan
            {
                SourcePath = pdfPath,
                RelativePath = relativePath,
                Status = BlankPageStatus.AllBlank,
                TotalPages = pageCount,
                BlankPageIndexes = blanks,
                Note = "Giu nguyen file goc de tranh mat du lieu, can kiem tra thu cong"
            };
        }

        return new BlankPageScan
        {
            SourcePath = pdfPath,
            RelativePath = relativePath,
            Status = BlankPageStatus.Removed,
            TotalPages = pageCount,
            BlankPageIndexes = blanks
        };
    }

    /// <summary>
    /// Mô tả lỗi để ghi vào cột ghi chú của báo cáo. Exception của WinRT khi mở PDF hỏng nhiều lúc có
    /// <c>Message</c> RỖNG (HRESULT không map được sang chuỗi), lúc đó phải rơi về tên kiểu + mã HRESULT
    /// — ô ghi chú trống thì người dùng không biết file hỏng vì lý do gì.
    /// </summary>
    private static string Describe(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message)
            ? $"{ex.GetType().Name} (HRESULT 0x{ex.HResult:X8})"
            : ex.Message.Trim();

    private static BlankPageScan Failed(string pdfPath, string relativePath, string note) => new()
    {
        SourcePath = pdfPath,
        RelativePath = relativePath,
        Status = BlankPageStatus.OpenError,
        Note = note
    };
}
