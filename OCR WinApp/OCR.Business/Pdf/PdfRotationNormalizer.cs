using PdfSharp.Pdf.IO;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinOcr = Windows.Media.Ocr;

namespace OCR.Business.Pdf;

/// <summary>
/// Chuan hoa huong PDF bang OCR local cua Windows.
/// Thu 4 huong OCR cho tung trang, roi chi cap nhat metadata Rotate de dua trang ve huong doc chuan.
/// </summary>
public sealed class PdfRotationNormalizer : IPdfRotationNormalizer
{
    private const double RenderScale = 1.5;

    public async Task<int> NormalizeAsync(string pdfPath, CancellationToken ct = default)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("Khong tim thay file PDF can xoay.", pdfPath);

        var engine = WinOcr.OcrEngine.TryCreateFromLanguage(new Language("vi"))
                     ?? WinOcr.OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException(
                         "Khong tao duoc OCR engine local de phat hien huong trang. Hay cai goi ngon ngu OCR trong Windows.");

        var detectionPath = CreateTempDetectionPath(pdfPath);
        try
        {
            File.Copy(pdfPath, detectionPath, overwrite: true);
            var corrections = await DetectCorrectionsByBestOcrAsync(detectionPath, engine, ct);
            if (corrections.Count == 0 || corrections.All(c => !c.HasValue || c.Value == 0))
                return 0;

            return ApplyCorrections(pdfPath, corrections);
        }
        finally
        {
            TryDelete(detectionPath);
        }
    }

    private static string CreateTempDetectionPath(string pdfPath)
    {
        var dir = Path.GetDirectoryName(pdfPath)!;
        var name = Path.GetFileNameWithoutExtension(pdfPath);
        return Path.Combine(dir, $"{name}.{Guid.NewGuid():N}.detect.tmp.pdf");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static async Task<List<int?>> DetectCorrectionsByBestOcrAsync(string pdfPath, WinOcr.OcrEngine engine, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var document = await PdfDocument.LoadFromFileAsync(file);
        var corrections = new List<int?>((int)document.PageCount);

        for (uint i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            using var page = document.GetPage(i);
            corrections.Add(await DetectBestCorrectionAsync(page, engine));
        }

        return corrections;
    }

    private static async Task<int?> DetectBestCorrectionAsync(PdfPage page, WinOcr.OcrEngine engine)
    {
        using var stream = await RenderPageStreamAsync(page);
        var candidates = new[]
        {
            new RotationCandidate(0, BitmapRotation.None),
            new RotationCandidate(90, BitmapRotation.Clockwise90Degrees),
            new RotationCandidate(180, BitmapRotation.Clockwise180Degrees),
            new RotationCandidate(270, BitmapRotation.Clockwise270Degrees)
        };

        OcrScore? best = null;
        foreach (var candidate in candidates)
        {
            OcrScore score;
            try
            {
                using var bitmap = await DecodeBitmapAsync(stream, candidate.BitmapRotation);
                var result = await engine.RecognizeAsync(bitmap);
                score = ScoreOcr(candidate.Correction, result);
            }
            catch
            {
                continue;
            }

            if (score.Score <= 0)
                continue;

            if (best is null || score.Score > best.Score)
                best = score;
        }

        return best?.Correction;
    }

    private static async Task<SoftwareBitmap> DecodeBitmapAsync(InMemoryRandomAccessStream stream, BitmapRotation rotation)
    {
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { Rotation = rotation };
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
    }

    private static OcrScore ScoreOcr(int correction, WinOcr.OcrResult result)
    {
        int textChars = result.Text.Count(char.IsLetterOrDigit);
        int lineCount = result.Lines.Count;
        int wordCount = result.Lines.Sum(line => line.Words.Count);
        int anglePenalty = result.TextAngle.HasValue ? AnglePenalty(result.TextAngle.Value) : 0;
        int score = wordCount * 1000 + lineCount * 100 + textChars - anglePenalty;
        return new OcrScore(correction, score);
    }

    private static int AnglePenalty(double textAngle)
    {
        var normalized = ((textAngle % 360) + 360) % 360;
        var distanceToHorizontal = Math.Min(normalized, 360 - normalized);
        return (int)Math.Round(distanceToHorizontal * 20, MidpointRounding.AwayFromZero);
    }

    private static async Task<InMemoryRandomAccessStream> RenderPageStreamAsync(PdfPage page)
    {
        var stream = new InMemoryRandomAccessStream();
        var size = page.Size;
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
        {
            DestinationWidth = Math.Max(1, (uint)(size.Width * RenderScale)),
            DestinationHeight = Math.Max(1, (uint)(size.Height * RenderScale))
        });
        stream.Seek(0);
        return stream;
    }

    private static int ApplyCorrections(string pdfPath, IReadOnlyList<int?> corrections)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(pdfPath)!,
            Path.GetFileNameWithoutExtension(pdfPath) + ".rotate.tmp.pdf");

        int changed = 0;
        using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
        {
            int count = Math.Min(doc.PageCount, corrections.Count);
            for (int i = 0; i < count; i++)
            {
                int correction = corrections[i].GetValueOrDefault();
                if (correction == 0) continue;

                var page = doc.Pages[i];
                page.Rotate = NormalizePageRotate(page.Rotate + correction);
                changed++;
            }

            if (changed == 0)
                return 0;

            doc.Save(tempPath);
        }

        File.Copy(tempPath, pdfPath, overwrite: true);
        File.Delete(tempPath);
        return changed;
    }

    private static int NormalizePageRotate(int degrees)
    {
        int normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private sealed record RotationCandidate(int Correction, BitmapRotation BitmapRotation);

    private sealed record OcrScore(int Correction, int Score);
}
