using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OCR.Business.UyBan;

/// <summary>
/// Tách PDF lớn → các PDF con 2 trang bằng PdfSharp (đồng bộ, gọi trong Task.Run từ ViewModel).
/// Tên PDF con tạm: "{tên file gốc}__{số thứ tự}.pdf" (đảm bảo duy nhất trong thư mục đích).
/// </summary>
public sealed class UyBanSplitService : IUyBanSplitService
{
    public IReadOnlyList<string> SplitInto2Pages(string pdfPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var stem = Sanitize(Path.GetFileNameWithoutExtension(pdfPath));

        using var src = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        int total = src.PageCount;

        var outputs = new List<string>();
        int part = 0;
        for (int start = 0; start < total; start += 2)
        {
            part++;
            using var outDoc = new PdfDocument();
            outDoc.AddPage(src.Pages[start]);
            if (start + 1 < total) outDoc.AddPage(src.Pages[start + 1]);

            var dest = UniquePath(outDir, $"{stem}__{part:D3}");
            outDoc.Save(dest);
            outputs.Add(dest);
        }
        return outputs;
    }

    /// <summary>Ghép đường dẫn .pdf duy nhất trong thư mục (thêm _1, _2... nếu trùng).</summary>
    private static string UniquePath(string dir, string stem)
    {
        var path = Path.Combine(dir, stem + ".pdf");
        int c = 1;
        while (File.Exists(path)) path = Path.Combine(dir, $"{stem}_{c++}.pdf");
        return path;
    }

    private static string Sanitize(string name)
    {
        name = Regex.Replace(name ?? "", @"[<>:""/\\|?*]", "");
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(name) ? "pdf" : name;
    }
}
