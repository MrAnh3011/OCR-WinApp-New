using OCR.Business.Export;
using OCR.Business.Models;
using OCR.Business.Ocr;
using OCR.Business.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace OCR.Business.Processors;

/// <summary>
/// Chức năng "OCR văn bản thường": quét mọi PDF/ảnh trong thư mục nguồn,
/// lấy toàn bộ text và ghi ra thư mục đích qua các <see cref="IResultExporter"/> đã đăng ký.
/// </summary>
public sealed class PlainTextProcessor : IDocumentProcessor
{
    private static readonly string[] PdfExtensions = { ".pdf" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        PdfExtensions.Concat(ImageExtensions).ToArray();

    private readonly IOcrEngine _ocr;
    private readonly IPdfRenderer _pdf;
    private readonly IEnumerable<IResultExporter> _exporters;

    public PlainTextProcessor(IOcrEngine ocr, IPdfRenderer pdf, IEnumerable<IResultExporter> exporters)
    {
        _ocr = ocr;
        _pdf = pdf;
        _exporters = exporters;
    }

    public DocumentFeature Feature { get; } = new()
    {
        Key = "plain-text",
        DisplayName = "OCR văn bản thường",
        Glyph = "", // Document
        Description = "Trích toàn bộ text từ tài liệu PDF/ảnh bất kỳ, không theo mẫu cố định."
    };

    public async Task<ProcessReport> ProcessAsync(
        string inputFolder,
        string outputFolder,
        ProcessOptions options,
        IProgress<ProcessProgress>? progress,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException($"Không tìm thấy thư mục nguồn: {inputFolder}");

        var files = Directory.EnumerateFiles(inputFolder)
            .Where(f => IsSupported(Path.GetExtension(f)))
            .ToList();

        return await ProcessFilesAsync(files, outputFolder, options, progress, ct);
    }

    public async Task<ProcessReport> ProcessFilesAsync(
        IReadOnlyList<string> files,
        string outputFolder,
        ProcessOptions options,
        IProgress<ProcessProgress>? progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);

        var supported = files.Where(f => IsSupported(Path.GetExtension(f))).ToList();
        var outcomes = new List<FileOutcome>();
        var total = supported.Count;
        var done = 0;
        progress?.Report(new ProcessProgress { Total = total, Completed = 0 });

        foreach (var file in supported)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            progress?.Report(new ProcessProgress { Total = total, Completed = done, CurrentFile = name });

            try
            {
                var result = await RecognizeFileAsync(file, options, ct);

                var outputs = new List<string>();
                var baseName = Path.GetFileNameWithoutExtension(file);
                foreach (var exporter in _exporters)
                {
                    var outPath = Path.Combine(outputFolder, baseName + exporter.Extension);
                    if (!options.Overwrite && File.Exists(outPath))
                        continue;
                    await exporter.ExportAsync(result, outPath, ct);
                    outputs.Add(outPath);
                }

                outcomes.Add(new FileOutcome
                {
                    FileName = name,
                    Status = FileStatus.Success,
                    OutputFiles = outputs
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                outcomes.Add(new FileOutcome
                {
                    FileName = name,
                    Status = FileStatus.Failed,
                    Message = ex.Message
                });
            }

            done++;
            progress?.Report(new ProcessProgress { Total = total, Completed = done, CurrentFile = name });
        }

        return new ProcessReport { Outcomes = outcomes };
    }

    private static bool IsSupported(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return PdfExtensions.Contains(ext) || ImageExtensions.Contains(ext);
    }

    private async Task<OcrResult> RecognizeFileAsync(string file, ProcessOptions options, CancellationToken ct)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var pages = new List<OcrPage>();

        if (PdfExtensions.Contains(ext))
        {
            var bitmaps = await _pdf.RenderPagesAsync(file, ct);
            var pageNo = 1;
            foreach (var bitmap in bitmaps)
            {
                ct.ThrowIfCancellationRequested();
                using (bitmap)
                {
                    pages.Add(await _ocr.RecognizeAsync(bitmap, pageNo++, options.Language, ct));
                }
            }
        }
        else
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(file);
            using var stream = await storageFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            pages.Add(await _ocr.RecognizeAsync(bitmap, 1, options.Language, ct));
        }

        return new OcrResult
        {
            FileName = Path.GetFileName(file),
            ProcessedAt = DateTimeOffset.Now,
            FullText = string.Join(Environment.NewLine + Environment.NewLine, pages.Select(p => p.FullText)),
            Pages = pages
        };
    }
}
