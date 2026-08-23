using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OCR.Business.IlisUb;

/// <summary>Kết quả gộp: số file đã gộp, số file bỏ qua vì hỏng, tổng số trang.</summary>
public sealed record PdfMergeResult(int MergedFiles, int SkippedFiles, int Pages);

/// <summary>
/// Gộp nhiều PDF thành một bằng PdfSharp. Cố ý KHÔNG dùng Python: dự án đóng gói bằng Inno Setup,
/// thêm runtime Python chỉ để nối trang là chi phí thừa và thêm điểm hỏng trên máy người dùng.
/// </summary>
public static class PdfMerger
{
    /// <summary>
    /// Gộp theo ĐÚNG thứ tự <paramref name="sourcePdfPaths"/>. File hỏng/không mở được, hoặc hỏng
    /// giữa chừng khi đọc trang, đều bị bỏ qua NGUYÊN CẢ FILE (báo qua <paramref name="onFileError"/>)
    /// chứ không làm hỏng cả lần gộp và không để lọt một phần trang của file hỏng vào kết quả. Không
    /// gộp được trang nào thì KHÔNG tạo file output.
    /// </summary>
    public static PdfMergeResult Merge(
        IReadOnlyList<string> sourcePdfPaths,
        string outputPath,
        Action<string, Exception>? onFileError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using var target = new PdfDocument();

        // PdfSharp import kiểu lazy: nội dung thật của trang (content stream, ảnh, font...) chỉ chắc
        // chắn được sao chép sang document đích tại thời điểm target.Save(). Vì vậy PHẢI giữ MỌI
        // document nguồn mở cho tới SAU khi Save xong rồi mới dispose — đúng pattern đã dùng trong
        // OCR.Business/Split/SplitGcnService.cs và OCR.Business/UyBan/UyBanSplitService.cs.
        var openSources = new List<PdfDocument>();
        int merged = 0, skipped = 0;

        try
        {
            foreach (var path in sourcePdfPaths)
            {
                try
                {
                    var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                    openSources.Add(source);
                    AppendPagesWithRollback(target, source.PageCount, i => source.Pages[i]);
                    merged++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    onFileError?.Invoke(path, ex);
                }
            }

            int totalPages = target.PageCount;
            if (totalPages == 0)
                return new PdfMergeResult(merged, skipped, 0);

            // Lưu ý: PdfSharp khoá PageCount sau khi Save() — phải đọc trước khi gọi Save.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            target.Save(outputPath);
            return new PdfMergeResult(merged, skipped, totalPages);
        }
        finally
        {
            // Dispose ngược thứ tự đã mở, sau khi Save (hoặc lỗi trong lúc Save) đã chạy xong.
            for (int i = openSources.Count - 1; i >= 0; i--)
                openSources[i].Dispose();
        }
    }

    /// <summary>
    /// Thêm <paramref name="pageCount"/> trang (lấy qua <paramref name="getPage"/>) vào
    /// <paramref name="target"/>. Nếu lỗi xảy ra giữa chừng (ví dụ trang thứ i hỏng), gỡ lại ĐÚNG các
    /// trang vừa thêm của lượt gọi này rồi ném lại lỗi cho người gọi xử lý — đảm bảo bỏ nguyên cả file
    /// hỏng thay vì để lọt một phần trang của nó vào kết quả gộp.
    /// Tách riêng thành hàm <c>internal</c> để unit-test trực tiếp đường rollback (xem
    /// <c>OCR.Business.Tests</c>), vì dựng một file PDF "mở được nhưng hỏng đúng giữa trang" một cách
    /// đáng tin cậy bằng PdfSharp là không khả thi trong thời gian hợp lý.
    /// </summary>
    internal static void AppendPagesWithRollback(PdfDocument target, int pageCount, Func<int, PdfPage> getPage)
    {
        int pageCountBefore = target.PageCount;
        try
        {
            for (int i = 0; i < pageCount; i++)
                target.AddPage(getPage(i));
        }
        catch
        {
            while (target.PageCount > pageCountBefore)
                target.Pages.RemoveAt(target.PageCount - 1);
            throw;
        }
    }
}
