using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OCR.Business.BlankPage;
using OCR.Business.Configuration;
using OCR.Business.Models;
using OCR_WinApp.ViewModels;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// Test cho màn "Xóa trang trắng": lõi nhận diện trên mảng điểm ảnh, bộ dựng cây kết quả + CSV,
// và hai hàm thuần của ViewModel (gom file, chặn thư mục đích nằm trong nguồn).
internal static partial class Program
{
    private static async Task RunBlankPageTests()
    {
        await BlankPageScannerFindsBlankPagesInRealPdf();
        await BlankPageScannerFlagsAllBlankAndUnreadablePdf();
        BlankPageOptionsParseSectionAndFallBackToDefaults();
        BlankPageDetectorTreatsCleanWhitePageAsBlank();
        BlankPageDetectorTreatsTextPageAsNotBlank();
        BlankPageDetectorIgnoresIsolatedScanSpecks();
        BlankPageDetectorIgnoresDarkScanBorder();
        BlankPageDetectorKeepsPageWithFaintStamp();
        BlankPageExporterRemovesOnlyBlankPagesAndKeepsTree();
        BlankPageExporterKeepsAllBlankAndUnreadablePdfIntact();
        BlankPageExporterRefusesToWriteWhenPageCountDisagrees();
        BlankPageExporterWritesUtf8BomCsvReport();
        BlankPageViewModelSplitsPdfsFromOtherFilesRecursively();
        BlankPageViewModelRejectsDestinationInsideSource();
    }

    private static BlankPageOptions DefaultBlankPageOptions() => new();

    // ------------------------------------------- luồng thật: render PDF rồi phán định

    /// <summary>
    /// Test XUYÊN SUỐT khâu rủi ro nhất: render trang bằng Windows.Data.Pdf → đổi BGRA sang xám →
    /// nhận diện. Lỗi hệ số render hay lỗi đổi màu sẽ hiện ra ở đây chứ không lọt tới người dùng.
    /// </summary>
    private static async Task BlankPageScannerFindsBlankPagesInRealPdf()
    {
        var root = NewTempDir();
        try
        {
            // Trang 1 và 3 có chữ, trang 2 và 4 để trắng hoàn toàn.
            var path = Path.Combine(root, "xen-ke.pdf");
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < 4; i++)
                {
                    var page = doc.AddPage();
                    if (i % 2 != 0) continue;
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString($"Trang co noi dung {i + 1}", new XFont("Arial", 24), XBrushes.Black,
                        new XPoint(60, 120));
                    gfx.DrawRectangle(XBrushes.Black, 60, 160, 300, 40);
                }
                doc.Save(path);
            }

            var scan = await new BlankPageScanner(DefaultBlankPageOptions())
                .ScanAsync(path, "xen-ke.pdf", CancellationToken.None);

            AssertEqual(BlankPageStatus.Removed, scan.Status, "PDF có trang trắng xen kẽ");
            AssertEqual(4, scan.TotalPages, "Tổng số trang");
            AssertEqual("2, 4", scan.DescribeBlankPages(), "Đúng hai trang trắng, đánh số từ 1");
            AssertEqual(2, scan.RemovedPageCount, "Số trang sẽ bị bỏ");
            AssertFalse(scan.NeedsReview, "PDF bình thường không vào nhóm cần kiểm tra tay");
        }
        finally { DeleteTempDir(root); }
    }

    private static async Task BlankPageScannerFlagsAllBlankAndUnreadablePdf()
    {
        var root = NewTempDir();
        try
        {
            var scanner = new BlankPageScanner(DefaultBlankPageOptions());

            var allBlank = Path.Combine(root, "trang-het.pdf");
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.AddPage();
                doc.Save(allBlank);
            }

            var blankScan = await scanner.ScanAsync(allBlank, "trang-het.pdf", CancellationToken.None);
            AssertEqual(BlankPageStatus.AllBlank, blankScan.Status, "PDF trắng toàn bộ");
            AssertEqual(0, blankScan.RemovedPageCount, "PDF trắng toàn bộ KHÔNG bỏ trang nào");
            AssertTrue(blankScan.NeedsReview, "PDF trắng toàn bộ phải vào nhóm cần kiểm tra tay");

            var broken = Path.Combine(root, "hong.pdf");
            File.WriteAllText(broken, "%PDF-1.4 day khong phai pdf that");

            var brokenScan = await scanner.ScanAsync(broken, "hong.pdf", CancellationToken.None);
            AssertEqual(BlankPageStatus.OpenError, brokenScan.Status, "PDF hỏng");
            AssertTrue(brokenScan.NeedsReview, "PDF hỏng phải vào nhóm cần kiểm tra tay");
            AssertTrue(brokenScan.Note.Length > 0, "PDF hỏng phải kèm lý do để ghi vào CSV");
        }
        finally { DeleteTempDir(root); }
    }

    // ---------------------------------------------------------------- cấu hình

    private static void BlankPageOptionsParseSectionAndFallBackToDefaults()
    {
        var fallback = new BlankPageOptions();
        AssertEqual(4, fallback.Workers, "BlankPage.Workers mặc định");
        AssertEqual(100, fallback.RenderDpi, "BlankPage.RenderDpi mặc định");
        AssertEqual(200, fallback.DarkPixelThreshold, "BlankPage.DarkPixelThreshold mặc định");
        AssertTrue(Math.Abs(fallback.CropMarginRatio - 0.03) < 1e-9, "BlankPage.CropMarginRatio mặc định");
        AssertTrue(Math.Abs(fallback.InkRatioThreshold - 0.0015) < 1e-9, "BlankPage.InkRatioThreshold mặc định");

        // File cấu hình thật đi kèm app phải đọc được và khớp với mặc định trong code.
        var loaded = AppSettingsLoader.LoadBlankPage();
        AssertTrue(loaded.Workers > 0, "BlankPage.Workers đọc từ appsettings phải > 0");
        AssertTrue(loaded.RenderDpi > 0, "BlankPage.RenderDpi đọc từ appsettings phải > 0");
        AssertTrue(loaded.InkRatioThreshold > 0, "BlankPage.InkRatioThreshold đọc từ appsettings phải > 0");
    }

    // ---------------------------------------------------------------- lõi nhận diện

    private const int PageWidth = 827;   // A4 @ 100 DPI, đúng kích thước tool Python sinh ra
    private const int PageHeight = 1169;

    private static byte[] WhitePage() => Enumerable.Repeat((byte)255, PageWidth * PageHeight).ToArray();

    /// <summary>Vẽ một khối chữ nhật tối đặc — mô phỏng nét chữ / đường kẻ thật.</summary>
    private static void FillDark(byte[] gray, int x0, int y0, int width, int height, byte value = 30)
    {
        for (int y = y0; y < y0 + height && y < PageHeight; y++)
            for (int x = x0; x < x0 + width && x < PageWidth; x++)
                gray[y * PageWidth + x] = value;
    }

    private static void BlankPageDetectorTreatsCleanWhitePageAsBlank()
    {
        AssertTrue(
            BlankPageDetector.IsBlank(WhitePage(), PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Trang trắng tinh phải bị coi là trang trắng.");
    }

    private static void BlankPageDetectorTreatsTextPageAsNotBlank()
    {
        var page = WhitePage();
        // 25 dòng chữ giả lập: mỗi dòng một dải đen 500x12.
        for (int i = 0; i < 25; i++) FillDark(page, 100, 100 + i * 40, 500, 12);

        AssertFalse(
            BlankPageDetector.IsBlank(page, PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Trang đầy chữ KHÔNG được coi là trang trắng.");
    }

    private static void BlankPageDetectorIgnoresIsolatedScanSpecks()
    {
        var page = WhitePage();
        // 300 hạt nhiễu 1 điểm ảnh rải rác — bộ lọc MAX 3x3 phải nuốt hết.
        var random = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            int x = random.Next(60, PageWidth - 60);
            int y = random.Next(60, PageHeight - 60);
            page[y * PageWidth + x] = 40;
        }

        AssertTrue(
            BlankPageDetector.IsBlank(page, PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Hạt nhiễu lấm tấm của bản scan phải bị khử, trang vẫn là trang trắng.");
    }

    private static void BlankPageDetectorIgnoresDarkScanBorder()
    {
        var page = WhitePage();
        // Viền đen dày 12px ở mép giấy — kiểu bóng do máy scan tạo ra. Nằm trong 3% bị cắt bỏ.
        FillDark(page, 0, 0, PageWidth, 12, 20);
        FillDark(page, 0, PageHeight - 12, PageWidth, 12, 20);
        FillDark(page, 0, 0, 12, PageHeight, 20);

        AssertTrue(
            BlankPageDetector.IsBlank(page, PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Viền đen ở mép giấy do scan phải bị cắt bỏ, trang vẫn là trang trắng.");
    }

    private static void BlankPageDetectorKeepsPageWithFaintStamp()
    {
        // Ca khó nhất, lấy từ bộ test của tool Python: trang CHỈ có một con dấu mờ (nét nhạt, độ xám
        // 110 — sát ngưỡng 200) và một dòng chữ nhỏ. Đây là trang có nội dung thật, xóa nhầm là mất
        // dữ liệu, nên phải kiểm tra riêng từng thành phần chứ không chỉ kiểm tra cả trang.
        var stampOnly = WhitePage();
        DrawFaintStamp(stampOnly);
        AssertFalse(
            BlankPageDetector.IsBlank(stampOnly, PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Trang chỉ có MỘT con dấu mờ vẫn là trang có nội dung, không được coi là trắng.");

        var stampAndText = WhitePage();
        DrawFaintStamp(stampAndText);
        FillDark(stampAndText, 120, 200, 520, 18, 60);
        AssertFalse(
            BlankPageDetector.IsBlank(stampAndText, PageWidth, PageHeight, DefaultBlankPageOptions()),
            "Trang có con dấu mờ + một dòng chữ nhỏ không được coi là trắng.");
    }

    /// <summary>Con dấu mờ: khung viền nét 6px, độ xám 110 — nhạt nhưng liền mạch nên qua được bộ lọc nhiễu.</summary>
    private static void DrawFaintStamp(byte[] page)
    {
        // Khung nằm gọn trong vùng đo (đã trừ 3% viền mỗi cạnh của khổ 827x1169).
        const byte faint = 110;
        FillDark(page, 450, 820, 350, 6, faint);   // cạnh trên
        FillDark(page, 450, 1064, 350, 6, faint);  // cạnh dưới
        FillDark(page, 450, 820, 6, 250, faint);   // cạnh trái
        FillDark(page, 794, 820, 6, 250, faint);   // cạnh phải
    }

    // ---------------------------------------------------------------- dựng cây kết quả

    /// <summary>Tạo PDF thật có <paramref name="pageCount"/> trang, mỗi trang ghi "Trang {i}".</summary>
    private static void CreateBlankPageTestPdf(string path, int pageCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var doc = new PdfDocument();
        for (int i = 1; i <= pageCount; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Trang {i}", new XFont("Arial", 24), XBrushes.Black,
                new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        }
        doc.Save(path);
    }

    private static int PageCountOf(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static BlankPageScan Scan(
        string root, string relative, BlankPageStatus status, int totalPages, params int[] blanks) => new()
    {
        SourcePath = Path.Combine(root, relative),
        RelativePath = relative,
        Status = status,
        TotalPages = totalPages,
        BlankPageIndexes = blanks
    };

    private static void BlankPageExporterRemovesOnlyBlankPagesAndKeepsTree()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dest = Path.Combine(root, "dest");

            CreateBlankPageTestPdf(Path.Combine(src, "HoSo01", "GCN.pdf"), 5);
            CreateBlankPageTestPdf(Path.Combine(src, "HoSo02", "GTK.pdf"), 2);
            Directory.CreateDirectory(Path.Combine(src, "HoSo02"));
            File.WriteAllText(Path.Combine(src, "HoSo02", "ghi-chu.txt"), "khong phai pdf");

            var scans = new List<BlankPageScan>
            {
                // Xóa trang 2 và 4 (chỉ số 1 và 3).
                Scan(src, Path.Combine("HoSo01", "GCN.pdf"), BlankPageStatus.Removed, 5, 1, 3),
                Scan(src, Path.Combine("HoSo02", "GTK.pdf"), BlankPageStatus.NoBlank, 2)
            };
            var others = new[] { Path.Combine(src, "HoSo02", "ghi-chu.txt") };

            var result = new BlankPageTreeExporter().Export(
                new BlankPageExportRequest(src, dest, scans, others));

            AssertEqual(1, result.PdfCleaned, "Số PDF đã bỏ trang trắng");
            AssertEqual(1, result.PdfCopied, "Số PDF copy nguyên");
            AssertEqual(1, result.OtherCopied, "Số tệp khác đã copy");
            AssertEqual(2, result.PagesRemoved, "Tổng số trang đã bỏ");
            AssertEqual(0, result.Failed, "Không có tệp lỗi");

            AssertEqual(3, PageCountOf(Path.Combine(dest, "HoSo01", "GCN.pdf")), "PDF sau khi bỏ 2 trang trắng");
            AssertEqual(2, PageCountOf(Path.Combine(dest, "HoSo02", "GTK.pdf")), "PDF không có trang trắng giữ đủ trang");

            // Cây đích phải khớp cây nguồn: cùng thư mục con, cùng tên file, kể cả file không phải PDF.
            AssertTrue(File.Exists(Path.Combine(dest, "HoSo02", "ghi-chu.txt")), "Tệp không phải PDF được copy nguyên");
            AssertEqual(
                File.ReadAllText(Path.Combine(src, "HoSo02", "ghi-chu.txt")),
                File.ReadAllText(Path.Combine(dest, "HoSo02", "ghi-chu.txt")),
                "Nội dung tệp không phải PDF giữ nguyên");

            // File copy nguyên phải giống hệt bản gốc từng byte.
            AssertTrue(
                File.ReadAllBytes(Path.Combine(src, "HoSo02", "GTK.pdf"))
                    .SequenceEqual(File.ReadAllBytes(Path.Combine(dest, "HoSo02", "GTK.pdf"))),
                "PDF không có trang trắng phải được copy nguyên từng byte");

            // Thư mục nguồn KHÔNG được đụng vào.
            AssertEqual(5, PageCountOf(Path.Combine(src, "HoSo01", "GCN.pdf")), "PDF nguồn giữ nguyên số trang");
        }
        finally { DeleteTempDir(root); }
    }

    private static void BlankPageExporterKeepsAllBlankAndUnreadablePdfIntact()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dest = Path.Combine(root, "dest");

            CreateBlankPageTestPdf(Path.Combine(src, "TrangHet.pdf"), 2);
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "Hong.pdf"), "%PDF-1.4 day khong phai pdf that");

            var scans = new List<BlankPageScan>
            {
                Scan(src, "TrangHet.pdf", BlankPageStatus.AllBlank, 2, 0, 1),
                Scan(src, "Hong.pdf", BlankPageStatus.OpenError, 0)
            };

            var result = new BlankPageTreeExporter().Export(
                new BlankPageExportRequest(src, dest, scans, Array.Empty<string>()));

            AssertEqual(0, result.PdfCleaned, "Không file nào bị bỏ trang");
            AssertEqual(2, result.PdfCopied, "Cả hai file được copy nguyên");
            AssertEqual(0, result.PagesRemoved, "Không trang nào bị bỏ");

            AssertEqual(2, PageCountOf(Path.Combine(dest, "TrangHet.pdf")),
                "PDF toàn trang trắng phải GIỮ NGUYÊN đủ trang, không xóa gì");
            AssertTrue(
                File.ReadAllBytes(Path.Combine(src, "Hong.pdf"))
                    .SequenceEqual(File.ReadAllBytes(Path.Combine(dest, "Hong.pdf"))),
                "PDF hỏng được copy nguyên từng byte");

            var csv = File.ReadAllText(FindReport(dest));
            AssertTrue(csv.Contains("CANH BAO - toan bo trang deu trang", StringComparison.Ordinal),
                "CSV phải ghi cảnh báo cho PDF toàn trang trắng");
            AssertTrue(csv.Contains("LOI - khong mo duoc PDF", StringComparison.Ordinal),
                "CSV phải ghi lỗi cho PDF hỏng");
        }
        finally { DeleteTempDir(root); }
    }

    private static void BlankPageExporterRefusesToWriteWhenPageCountDisagrees()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dest = Path.Combine(root, "dest");
            CreateBlankPageTestPdf(Path.Combine(src, "LechTrang.pdf"), 4);

            // Bộ đọc lúc quét báo 6 trang, PdfSharp lúc ghi thấy 4 → chỉ số trang trắng không còn đáng
            // tin, phải bỏ qua việc ghi và copy nguyên bản gốc thay vì cắt nhầm trang có nội dung.
            var scans = new List<BlankPageScan>
            {
                Scan(src, "LechTrang.pdf", BlankPageStatus.Removed, 6, 1, 5)
            };

            var result = new BlankPageTreeExporter().Export(
                new BlankPageExportRequest(src, dest, scans, Array.Empty<string>()));

            AssertEqual(0, result.PdfCleaned, "Số trang lệch thì KHÔNG ghi bản mới");
            AssertEqual(1, result.PdfCopied, "Số trang lệch thì copy nguyên bản gốc");
            AssertEqual(4, PageCountOf(Path.Combine(dest, "LechTrang.pdf")), "Bản gốc giữ đủ 4 trang");

            var csv = File.ReadAllText(FindReport(dest));
            AssertTrue(csv.Contains("da copy nguyen ban goc", StringComparison.Ordinal),
                "CSV phải nói rõ đã copy nguyên bản gốc khi không ghi được");
        }
        finally { DeleteTempDir(root); }
    }

    private static void BlankPageExporterWritesUtf8BomCsvReport()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dest = Path.Combine(root, "dest");
            CreateBlankPageTestPdf(Path.Combine(src, "Hồ sơ", "GCN, bản chính.pdf"), 3);

            var scans = new List<BlankPageScan>
            {
                Scan(src, Path.Combine("Hồ sơ", "GCN, bản chính.pdf"), BlankPageStatus.Removed, 3, 1)
            };

            new BlankPageTreeExporter().Export(new BlankPageExportRequest(src, dest, scans, Array.Empty<string>()));

            var reportPath = FindReport(dest);
            var bytes = File.ReadAllBytes(reportPath);
            AssertTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "CSV phải có BOM UTF-8 để Excel mở không lỗi font tiếng Việt");

            var lines = File.ReadAllLines(reportPath, Encoding.UTF8);
            AssertEqual("duong_dan,tong_trang,so_trang_xoa,cac_trang_da_xoa,trang_thai,ghi_chu", lines[0],
                "Hàng tiêu đề CSV");
            // Đường dẫn có dấu phẩy phải được bọc nháy kép, nếu không cột sẽ bị lệch khi mở bằng Excel.
            AssertTrue(lines[1].StartsWith("\"Hồ sơ\\GCN, bản chính.pdf\",", StringComparison.Ordinal),
                "Giá trị chứa dấu phẩy phải được bọc nháy kép");
            AssertTrue(lines[1].Contains(",3,1,2,DA XOA TRANG TRANG", StringComparison.Ordinal),
                "CSV ghi tổng trang / số trang xóa / số thứ tự trang đã xóa (đếm từ 1)");
        }
        finally { DeleteTempDir(root); }
    }

    private static string FindReport(string dest)
        => Directory.GetFiles(dest, "bao-cao-xoa-trang-trang_*.csv").Single();

    // ---------------------------------------------------------------- hàm thuần của ViewModel

    private static void BlankPageViewModelSplitsPdfsFromOtherFilesRecursively()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(CreatePath(root, "a.pdf"), "x");
            File.WriteAllText(CreatePath(root, "con", "b.PDF"), "x");
            File.WriteAllText(CreatePath(root, "con", "sau", "c.pdf"), "x");
            File.WriteAllText(CreatePath(root, "con", "anh.png"), "x");
            File.WriteAllText(CreatePath(root, "ghi-chu.txt"), "x");

            var (pdfs, others) = XoaTrangTrangViewModel.CollectFiles(root);

            AssertEqual(3, pdfs.Count, "Quét đệ quy phải thấy đủ 3 PDF ở mọi cấp");
            AssertTrue(pdfs.Any(p => p.EndsWith("b.PDF", StringComparison.OrdinalIgnoreCase)),
                "Đuôi .PDF viết hoa vẫn phải được nhận");
            AssertEqual(2, others.Count, "Hai tệp không phải PDF vào nhóm copy nguyên");
            AssertTrue(others.Any(p => p.EndsWith("anh.png", StringComparison.OrdinalIgnoreCase)),
                "Ảnh nằm trong nhóm copy nguyên, không đem đi phân tích");
        }
        finally { DeleteTempDir(root); }
    }

    private static void BlankPageViewModelRejectsDestinationInsideSource()
    {
        AssertTrue(XoaTrangTrangViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo\KetQua"),
            "Thư mục đích nằm trong nguồn phải bị chặn");
        AssertTrue(XoaTrangTrangViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo"),
            "Thư mục đích trùng nguồn phải bị chặn");
        AssertFalse(XoaTrangTrangViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo2"),
            "Thư mục cùng tiền tố nhưng khác hẳn thì KHÔNG được chặn nhầm");
        AssertFalse(XoaTrangTrangViewModel.IsDestinationInsideSource(@"E:\HoSo", @"D:\KetQua"),
            "Thư mục đích ở ổ khác thì cho chạy");
        AssertFalse(XoaTrangTrangViewModel.IsDestinationInsideSource(null, @"D:\KetQua"),
            "Chưa chọn nguồn thì không chặn");
    }

    // ---------------------------------------------------------------- tiện ích

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempDir(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static string CreatePath(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
