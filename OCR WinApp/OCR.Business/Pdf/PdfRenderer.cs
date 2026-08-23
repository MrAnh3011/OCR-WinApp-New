using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OCR.Business.Pdf;

/// <summary>Render trang PDF ra ảnh bằng Windows.Data.Pdf (không cần thư viện ngoài).</summary>
public sealed class PdfRenderer : IPdfRenderer
{
    /// <summary>Hệ số phóng to khi render để tăng độ chính xác OCR.</summary>
    private const double Scale = 2.0;

    public async Task<IReadOnlyList<SoftwareBitmap>> RenderPagesAsync(string pdfPath, CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var document = await PdfDocument.LoadFromFileAsync(file);

        var bitmaps = new List<SoftwareBitmap>();
        for (uint i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            using var page = document.GetPage(i);
            using var stream = new InMemoryRandomAccessStream();

            var size = page.Size;
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)(size.Width * Scale),
                DestinationHeight = (uint)(size.Height * Scale)
            };

            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            bitmaps.Add(await decoder.GetSoftwareBitmapAsync());
        }

        return bitmaps;
    }

    public async Task<IReadOnlyList<byte[]>> RenderPagesJpegAsync(string pdfPath, CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var document = await PdfDocument.LoadFromFileAsync(file);

        var result = new List<byte[]>();
        for (uint i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);
            result.Add(await EncodePageJpegAsync(page));
        }
        return result;
    }

    public async Task<byte[]> RenderPageJpegAsync(string pdfPath, int pageIndex = 0, CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var document = await PdfDocument.LoadFromFileAsync(file);
        if (document.PageCount == 0) throw new InvalidOperationException("PDF không có trang nào.");

        var idx = (uint)Math.Clamp(pageIndex, 0, (int)document.PageCount - 1);
        using var page = document.GetPage(idx);
        return await EncodePageJpegAsync(page);
    }

    /// <summary>Render 1 trang PDF → JPEG (byte[]). RenderToStreamAsync xuất bitmap nên cần re-encode sang JPEG cho API.</summary>
    private static async Task<byte[]> EncodePageJpegAsync(PdfPage page)
    {
        using var raw = new InMemoryRandomAccessStream();
        var size = page.Size;
        await page.RenderToStreamAsync(raw, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)(size.Width * Scale),
            DestinationHeight = (uint)(size.Height * Scale)
        });
        raw.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(raw);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        using var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        using var outStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetSoftwareBitmap(converted);
        await encoder.FlushAsync();

        outStream.Seek(0);
        var bytes = new byte[outStream.Size];
        await outStream.ReadAsync(bytes.AsBuffer(), (uint)outStream.Size, InputStreamOptions.None);
        return bytes;
    }
}
