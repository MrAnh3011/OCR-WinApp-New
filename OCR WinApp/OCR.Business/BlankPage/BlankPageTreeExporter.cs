using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OCR.Business.BlankPage;

/// <summary>
/// Dựng cây thư mục kết quả: giữ đúng 100% cấu trúc và tên so với thư mục nguồn.
///   - PDF có trang trắng  → ghi bản mới đã bỏ các trang đó.
///   - PDF còn lại         → copy nguyên bản gốc (không trang trắng / trắng toàn bộ / hỏng).
///   - File không phải PDF → copy nguyên.
/// Cuối cùng ghi một file CSV thống kê ở gốc thư mục đích.
/// Không đụng gì vào thư mục nguồn.
/// </summary>
public sealed class BlankPageTreeExporter : IBlankPageTreeExporter
{
    private static readonly string[] CsvHeader =
        ["duong_dan", "tong_trang", "so_trang_xoa", "cac_trang_da_xoa", "trang_thai", "ghi_chu"];

    public BlankPageExportResult Export(BlankPageExportRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRoot);

        Directory.CreateDirectory(request.DestinationRoot);

        var result = new BlankPageExportResult();
        var rows = new List<string[]>();

        foreach (var scan in request.PdfScans)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ExportOnePdf(scan, request.DestinationRoot, result));
        }

        foreach (var source in request.OtherFiles)
        {
            ct.ThrowIfCancellationRequested();

            var relative = Relative(request.SourceRoot, source);
            try
            {
                CopyOriginal(source, Path.Combine(request.DestinationRoot, relative));
                result.OtherCopied++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                rows.Add([relative, "", "", "", "LOI - khong copy duoc file", ex.Message]);
            }
        }

        result.ReportPath = WriteReport(request.DestinationRoot, rows);
        return result;
    }

    private static string[] ExportOnePdf(BlankPageScan scan, string destinationRoot, BlankPageExportResult result)
    {
        var dest = Path.Combine(destinationRoot, scan.RelativePath);

        if (scan.Status != BlankPageStatus.Removed)
        {
            try
            {
                CopyOriginal(scan.SourcePath, dest);
                result.PdfCopied++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                return Row(scan, "LOI - khong copy duoc file", ex.Message, removed: 0);
            }

            return scan.Status switch
            {
                BlankPageStatus.NoBlank => Row(scan, "OK - khong co trang trang", scan.Note, removed: 0),
                BlankPageStatus.AllBlank => Row(scan, "CANH BAO - toan bo trang deu trang", scan.Note, removed: 0),
                _ => Row(scan, "LOI - khong mo duoc PDF", scan.Note, removed: 0)
            };
        }

        try
        {
            WriteWithoutPages(scan.SourcePath, dest, scan.BlankPageIndexes, scan.TotalPages);
            result.PdfCleaned++;
            result.PagesRemoved += scan.BlankPageIndexes.Count;
            return Row(scan, "DA XOA TRANG TRANG", scan.Note, scan.BlankPageIndexes.Count);
        }
        catch (Exception ex)
        {
            // Ghi bản mới hỏng thì thà copy nguyên bản gốc còn hơn để khuyết file ở cây đích.
            try
            {
                CopyOriginal(scan.SourcePath, dest);
                result.PdfCopied++;
            }
            catch { result.Failed++; }

            return Row(scan, "LOI - khong ghi duoc PDF, da copy nguyen ban goc", ex.Message, removed: 0);
        }
    }

    /// <summary>
    /// Ghi bản PDF mới không còn các trang trong <paramref name="blankPageIndexes"/>.
    /// PdfSharp import kiểu lazy: nội dung thật của trang chỉ được sao chép sang document đích tại thời
    /// điểm Save(), nên PHẢI giữ document nguồn mở cho tới sau khi Save xong — cùng pattern với
    /// <c>IlisUb/PdfMerger.cs</c> và <c>UyBan/UyBanSplitService.cs</c>.
    /// </summary>
    internal static void WriteWithoutPages(
        string sourcePath, string destPath, IReadOnlyList<int> blankPageIndexes, int expectedPageCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);

        using var input = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);

        // Hai bộ đọc PDF khác nhau (Windows.Data.Pdf lúc quét, PdfSharp lúc ghi) mà đếm ra số trang
        // lệch nhau thì chỉ số trang trắng không còn đáng tin — dừng lại để bên gọi copy nguyên bản gốc.
        if (expectedPageCount > 0 && input.PageCount != expectedPageCount)
        {
            throw new InvalidOperationException(
                $"So trang khong khop khi ghi lai PDF: luc quet {expectedPageCount}, luc ghi {input.PageCount}.");
        }

        var blanks = new HashSet<int>(blankPageIndexes);
        using var output = new PdfDocument();
        for (int i = 0; i < input.PageCount; i++)
            if (!blanks.Contains(i)) output.AddPage(input.Pages[i]);

        if (output.PageCount == 0)
            throw new InvalidOperationException("Ban PDF moi khong con trang nao.");

        output.Save(destPath);
    }

    private static void CopyOriginal(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);
        File.Copy(source, dest, overwrite: true);
    }

    private static string Relative(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.StartsWith("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                return Path.GetFileName(path);
            }
            return relative;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static string[] Row(BlankPageScan scan, string status, string note, int removed) =>
    [
        scan.RelativePath,
        scan.Status == BlankPageStatus.OpenError ? "" : scan.TotalPages.ToString(CultureInfo.InvariantCulture),
        scan.Status == BlankPageStatus.OpenError ? "" : removed.ToString(CultureInfo.InvariantCulture),
        scan.DescribeBlankPages(),
        status,
        note
    ];

    /// <summary>Ghi CSV UTF-8 CÓ BOM để mở thẳng bằng Excel không lỗi font tiếng Việt.</summary>
    private static string WriteReport(string destinationRoot, IReadOnlyList<string[]> rows)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(destinationRoot, $"bao-cao-xoa-trang-trang_{stamp}.csv");

        var text = new StringBuilder();
        text.AppendLine(string.Join(',', CsvHeader.Select(Escape)));
        foreach (var row in rows) text.AppendLine(string.Join(',', row.Select(Escape)));

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static string Escape(string? value)
    {
        var text = value ?? "";
        if (text.IndexOfAny([',', '"', '\n', '\r']) < 0) return text;
        return '"' + text.Replace("\"", "\"\"") + '"';
    }
}
