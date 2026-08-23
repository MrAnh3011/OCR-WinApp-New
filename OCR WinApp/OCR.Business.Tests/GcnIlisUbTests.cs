using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OCR.Business.Configuration;
using OCR.Business.IlisUb;
using OCR.Business.Models;
using OCR_WinApp.ViewModels;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// Test cho màn OCR GCN iLis-UB: quy tắc so khớp tên file, gộp PDF và dựng cây thư mục Export.
internal static partial class Program
{
    private static void RunGcnIlisUbTests()
    {
        IlisUbOptionsParsesSectionAndFallsBackToDefaults();
        IlisUbRulesNormalizeStripsVietnameseAccentsAndCase();
        IlisUbRulesDetectGcnFileNameCaseAndAccentInsensitive();
        IlisUbRulesFirstMatchingGroupWins();
        IlisUbRulesReportKeywordIndexForOrdering();
        IlisUbRulesLoaderRejectsBadFiles();
        IlisUbRulesLoaderRejectsSuffixMatchingGcnKeyword();
        IlisUbRulesLoaderReadsShippedDefaultFile();
        PdfMergerKeepsGivenOrderAndCountsPages();
        PdfMergerSkipsCorruptFilesWithoutFailingWholeMerge();
        PdfMergerMergesPagesWithRealContentAndStaysReadable();
        PdfMergerRollsBackPartiallyAddedPagesOfFailingFile();
        TreeExporterCreatesOneFolderPerGcnAsSiblings();
        TreeExporterSuffixesDuplicateSerialAndNamesFilesAfterFolder();
        TreeExporterFallsBackToSourceFolderNameWhenSerialMissing();
        TreeExporterSkipsFoldersWithoutSuccessfulGcn();
        TreeExporterSuffixesRootWhenDestinationAlreadyExists();
        TreeExporterSuffixesDuplicateSerialAcrossDifferentHoSoFolders();
        TreeExporterMergesAttachmentsByRuleOrderIntoEveryLabelFolder();
        TreeExporterCopiesUnmatchedAndNonPdfFilesAsIs();
        TreeExporterCopiesSubFolderWithoutGcnAndSkipsSubFolderWithGcn();
        TreeExporterNeverPutsChildHoSoGcnInsideParentLabelFolder();
        TreeExporterKeepsScannedGcnOutOfAttachmentsWhenKeywordChanged();
        TreeExporterWarnsWhenSkippingSubFolderThatNeverBecomesHoSo();
        TreeExporterKeepsDriveRootSourcePathIntact();
        TreeExporterLimitsAttachmentFailureToOneLabelFolder();
        TreeExporterCreatesNoMergedFileWhenKeywordsAreEmpty();
        TreeExporterTreatsUnreadableSubFolderAsPossiblyContainingGcn();
        ViewModelOnlyAcceptsPdfFilesContainingGcnKeyword();
        ViewModelRejectsDestinationInsideSourceFolder();
        ViewModelDoesNotRenameSourceFilesOnExport();
        FailedHoSoCollectorTakesWholeFolderNotJustGcn();
        FailedHoSoCollectorExcludesWhatAlreadySucceeded();
        FailedHoSoCollectorHandlesNestedFailedHoSoWithoutDuplicating();
    }

    /// <summary>
    /// Nút "File lỗi" phải gom CẢ THƯ MỤC HỒ SƠ chứa GCN lỗi (GT/GTK/ảnh/thư mục con), không chỉ mỗi
    /// file GCN — chạy lại mới có đủ dữ liệu.
    /// </summary>
    private static void FailedHoSoCollectorTakesWholeFolderNotJustGcn()
    {
        var root = NewIlisUbTempDir();
        try
        {
            var hoSo = Path.Combine(root, "Phuong1", "HoSo05");
            WriteIlisUbFile(hoSo, "GCN-123.pdf");
            WriteIlisUbFile(hoSo, "GT so 1.pdf");
            WriteIlisUbFile(hoSo, "GTK.pdf");
            WriteIlisUbFile(hoSo, "ghi-chu.txt");
            WriteIlisUbFile(Path.Combine(hoSo, "Anh"), "scan01.jpg");

            // Hồ sơ khác, KHÔNG lỗi -> không được lọt vào kết quả.
            WriteIlisUbFile(Path.Combine(root, "Phuong1", "HoSo06"), "GCN-999.pdf");

            var result = FailedHoSoFileCollector.Collect(
                [Path.Combine(hoSo, "GCN-123.pdf")],
                []);

            AssertEqual(1, result.HoSoFolders, "Một thư mục hồ sơ lỗi");
            AssertEqual(5, result.Files.Count, "Gom đủ 5 file của cả thư mục hồ sơ, kể cả thư mục con");
            foreach (var name in new[] { "GCN-123.pdf", "GT so 1.pdf", "GTK.pdf", "ghi-chu.txt", "scan01.jpg" })
            {
                AssertTrue(result.Files.Any(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)),
                    $"Phải gom file \"{name}\" của thư mục hồ sơ lỗi");
            }
            AssertFalse(result.Files.Any(f => f.Contains("HoSo06", StringComparison.OrdinalIgnoreCase)),
                "Hồ sơ không lỗi KHÔNG được lọt vào tập file xuất ra.");
            AssertEqual(0, result.Warnings.Count, "Không có cảnh báo");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    /// <summary>
    /// Hai phép loại trừ: file GCN đã đọc được nằm cùng thư mục thì bỏ; thư mục con là hồ sơ đã xử lý
    /// xong thì bỏ cả thư mục. Mục đích: chạy lại không OCR lại thứ đã xong, không tốn thêm hạn mức.
    /// </summary>
    private static void FailedHoSoCollectorExcludesWhatAlreadySucceeded()
    {
        var root = NewIlisUbTempDir();
        try
        {
            var hoSo = Path.Combine(root, "HoSo01");
            var failedGcn = WriteIlisUbFile(hoSo, "GCN-loi.pdf");
            var okGcn = WriteIlisUbFile(hoSo, "GCN-da-doc-duoc.pdf");
            WriteIlisUbFile(hoSo, "GT.pdf");

            // Thư mục con là hồ sơ ĐÃ XỬ LÝ XONG -> bỏ cả thư mục, kể cả file kèm theo bên trong.
            var subDone = Path.Combine(hoSo, "HoSoCon-xong");
            var subDoneGcn = WriteIlisUbFile(subDone, "GCN-777.pdf");
            WriteIlisUbFile(subDone, "GTK.pdf");

            // Thư mục con KHÔNG có GCN nào -> là phần của hồ sơ lỗi, vẫn phải gom.
            WriteIlisUbFile(Path.Combine(hoSo, "Anh"), "mat-truoc.jpg");

            var result = FailedHoSoFileCollector.Collect([failedGcn], [okGcn, subDoneGcn]);

            AssertTrue(result.Files.Any(f => f.EndsWith("GCN-loi.pdf", StringComparison.OrdinalIgnoreCase)),
                "File GCN lỗi vẫn phải được gom");
            AssertTrue(result.Files.Any(f => f.EndsWith("GT.pdf", StringComparison.OrdinalIgnoreCase)),
                "File kèm theo của hồ sơ lỗi phải được gom");
            AssertTrue(result.Files.Any(f => f.EndsWith("mat-truoc.jpg", StringComparison.OrdinalIgnoreCase)),
                "Thư mục con không chứa GCN là phần của hồ sơ lỗi, phải được gom");

            AssertFalse(result.Files.Any(f => f.EndsWith("GCN-da-doc-duoc.pdf", StringComparison.OrdinalIgnoreCase)),
                "GCN đã OCR thành công cùng thư mục phải bị LOẠI.");
            AssertFalse(result.Files.Any(f => f.Contains("HoSoCon-xong", StringComparison.OrdinalIgnoreCase)),
                "Thư mục con là hồ sơ đã xử lý xong phải bị bỏ CẢ THƯ MỤC, kể cả file kèm theo.");
            AssertEqual(3, result.Files.Count, "Đúng 3 file sau khi loại trừ");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    /// <summary>
    /// Hồ sơ lỗi lồng trong hồ sơ lỗi: thư mục cha đã đi qua thư mục con, lượt riêng của thư mục con
    /// không được làm file bị gom hai lần.
    /// </summary>
    private static void FailedHoSoCollectorHandlesNestedFailedHoSoWithoutDuplicating()
    {
        var root = NewIlisUbTempDir();
        try
        {
            var parent = Path.Combine(root, "HoSoCha");
            var child = Path.Combine(parent, "HoSoCon");
            var parentGcn = WriteIlisUbFile(parent, "GCN-cha.pdf");
            var childGcn = WriteIlisUbFile(child, "GCN-con.pdf");
            WriteIlisUbFile(child, "GT-con.pdf");

            var result = FailedHoSoFileCollector.Collect([childGcn, parentGcn], []);

            AssertEqual(2, result.HoSoFolders, "Hai thư mục hồ sơ lỗi lồng nhau");
            AssertEqual(3, result.Files.Count, "Mỗi file gom đúng MỘT lần, không nhân đôi");
            AssertEqual(result.Files.Count, result.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                "Danh sách file không được có phần tử trùng");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static string NewIlisUbTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteIlisUbTempDir(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static string WriteIlisUbFile(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "noi dung test");
        return path;
    }

    private static void ViewModelOnlyAcceptsPdfFilesContainingGcnKeyword()
    {
        // Bộ lọc đầu vào là một hàm static thuần để test được mà không cần dựng cả ViewModel/WinUI.
        var rules = new GcnFolderRules { GcnKeyword = "GCN" };

        AssertTrue(GcnIlisUbViewModel.IsAcceptedSourceFile("C:\\x\\GCN-719822.pdf", rules), "PDF có GCN hoa");
        AssertTrue(GcnIlisUbViewModel.IsAcceptedSourceFile("C:\\x\\scan gcn 01.PDF", rules), "PDF có gcn thường, đuôi hoa");
        AssertFalse(GcnIlisUbViewModel.IsAcceptedSourceFile("C:\\x\\don dang ky.pdf", rules), "PDF không có GCN");
        AssertFalse(GcnIlisUbViewModel.IsAcceptedSourceFile("C:\\x\\GCN-719822.jpg", rules), "Ảnh bị loại dù tên có GCN");
    }

    /// <summary>
    /// I1 — người dùng chọn nguồn `E:\HoSo` rồi Export cũng chọn `E:\HoSo`: cây kết quả bị ghi vào giữa
    /// thư mục nguồn, lần quét sau nuốt luôn các `{nhãn}-GCN.pdf` vừa sinh làm nguồn OCR mới ⇒ nhân đôi
    /// dữ liệu và tốn quota. Phải chặn TRƯỚC khi ghi bất cứ thứ gì.
    /// </summary>
    private static void ViewModelRejectsDestinationInsideSourceFolder()
    {
        AssertTrue(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo"),
            "Đích TRÙNG nguồn phải bị chặn");
        AssertTrue(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo\KetQua"),
            "Đích nằm ngay dưới nguồn phải bị chặn");
        AssertTrue(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo\a\b\c"),
            "Đích nằm sâu bên dưới nguồn phải bị chặn");
        AssertTrue(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo\", @"e:\hoso\ketqua\"),
            "Chuẩn hoá dấu phân cách cuối + không phân biệt hoa/thường");

        // Thư mục ANH EM có tên bắt đầu bằng tên nguồn KHÔNG được chặn nhầm.
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo2"),
            "Thư mục anh em cùng tiền tố không bị chặn nhầm");
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\KetQua"),
            "Thư mục khác cùng ổ đĩa được phép");
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", @"D:\KetQua"),
            "Thư mục khác ổ đĩa được phép");
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo\KetQua", @"E:\HoSo"),
            "Nguồn nằm trong đích thì KHÔNG chặn (chỉ chặn chiều ngược lại)");
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(null, @"E:\KetQua"), "Nguồn rỗng → không chặn");
        AssertFalse(GcnIlisUbViewModel.IsDestinationInsideSource(@"E:\HoSo", "   "), "Đích rỗng → không chặn");

        // Khoá THỨ TỰ: phải chặn trước khi ghi Excel/dựng cây, nếu không vẫn kịp bẩn thư mục nguồn.
        var path = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnIlisUbViewModel.cs"));
        var code = File.ReadAllText(path);
        var guard = code.IndexOf("if (IsDestinationInsideSource(sourceRoot, destDir))", StringComparison.Ordinal);
        var writeExcel = code.IndexOf("_excel.Write(", StringComparison.Ordinal);
        var buildTree = code.IndexOf("_treeExporter.Export(", StringComparison.Ordinal);
        AssertTrue(guard > 0, "ExportAsync phải kiểm thư mục đích nằm trong nguồn");
        AssertTrue(writeExcel > guard && buildTree > guard,
            "Phép kiểm phải chạy TRƯỚC khi ghi Excel và dựng cây thư mục");

        // CẢ HAI điểm cho người dùng chọn thư mục đích đều phải chặn: nút "File lỗi" copy ra chính các
        // file GCN OCR lỗi (tên vẫn chứa "GCN"), rải vào giữa thư mục nguồn là lần quét sau nhận làm
        // file đầu vào — đúng vòng lặp nhân đôi dữ liệu mà phép kiểm này tồn tại để chặn.
        var failedStart = code.IndexOf("private async Task ExportFailedSourcesAsync()", StringComparison.Ordinal);
        var exportStart = code.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(failedStart > 0 && exportStart > failedStart,
            "Kỳ vọng ExportFailedSourcesAsync đứng trước ExportAsync trong file");

        var failedBody = code[failedStart..exportStart];
        var failedGuard = failedBody.IndexOf("IsDestinationInsideSource(SelectedPath, destDir)", StringComparison.Ordinal);
        var copyFailed = failedBody.IndexOf("FailedSourceFileExporter.CopyFiles", StringComparison.Ordinal);
        AssertTrue(failedGuard > 0, "Nút \"File lỗi\" cũng phải kiểm thư mục đích nằm trong nguồn");
        AssertTrue(copyFailed > failedGuard, "Phép kiểm phải chạy TRƯỚC khi copy file lỗi");

        // Dùng LẠI đúng một hàm, không viết bản thứ hai dễ lệch hành vi giữa hai điểm gọi.
        AssertEqual(1,
            CountOccurrences(code, "internal static bool IsDestinationInsideSource"),
            "Chỉ được có ĐÚNG MỘT hàm kiểm thư mục đích nằm trong nguồn");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static void ViewModelDoesNotRenameSourceFilesOnExport()
    {
        // Bảo vệ khác biệt nghiệp vụ quan trọng nhất so với màn iLIS: KHÔNG đổi tên file nguồn.
        var path = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnIlisUbViewModel.cs"));

        var code = File.ReadAllText(path);
        AssertFalse(code.Contains("File.Move(", StringComparison.Ordinal), "ViewModel không được File.Move file nguồn");
        AssertFalse(code.Contains("Directory.Move(", StringComparison.Ordinal), "ViewModel không được Directory.Move thư mục nguồn");
        AssertFalse(code.Contains("File.Delete(", StringComparison.Ordinal), "ViewModel không được xoá file nguồn");
        AssertFalse(code.Contains("Directory.Delete(", StringComparison.Ordinal), "ViewModel không được xoá thư mục nguồn");
        AssertFalse(code.Contains("RenameSuccessfulSourceFiles", StringComparison.Ordinal), "Đã bỏ hàm đổi tên file nguồn của màn iLIS");
        AssertTrue(code.Contains("\"gcn-ilis-ub\"", StringComparison.Ordinal), "Dùng đúng screen key gcn-ilis-ub");
        AssertFalse(code.Contains("\"gcn-new\"", StringComparison.Ordinal), "Không còn sót screen key gcn-new");
        AssertTrue(code.Contains("IGcnTreeExporter", StringComparison.Ordinal), "Export đi qua IGcnTreeExporter");
    }

    private static void IlisUbOptionsParsesSectionAndFallsBackToDefaults()
    {
        var opt = new GcnIlisUbOptions();
        AssertEqual(5, opt.Workers, "GcnIlisUbOptions.Workers mặc định");
        AssertFalse(opt.OptimizeImages, "GcnIlisUbOptions.OptimizeImages mặc định");
        AssertEqual(Path.Combine("Assets", "Temp", "Excel_FormMau_v3.xlsx"), opt.TemplateExcel, "GcnIlisUbOptions.TemplateExcel mặc định");
        AssertEqual("gcn-ilis-ub-rules.json", opt.RulesFile, "GcnIlisUbOptions.RulesFile mặc định");

        // LoadIlisUbGcn đọc file cạnh exe; ở test harness file đó không tồn tại nên phải trả default,
        // KHÔNG được ném exception (mọi Load* của AppSettingsLoader đều nuốt lỗi).
        var loaded = AppSettingsLoader.LoadIlisUbGcn();
        AssertTrue(loaded.Workers >= 1, "LoadIlisUbGcn trả Workers hợp lệ");
        AssertTrue(!string.IsNullOrWhiteSpace(loaded.RulesFile), "LoadIlisUbGcn trả RulesFile không rỗng");
    }

    private static void IlisUbRulesNormalizeStripsVietnameseAccentsAndCase()
    {
        AssertEqual("DON DANG KY", GcnFolderRules.Normalize(" Đơn đăng ký "), "Normalize bỏ dấu + hoa hoá");
        AssertEqual("TRICH LUC", GcnFolderRules.Normalize("Trích lục"), "Normalize trích lục");
        AssertEqual("", GcnFolderRules.Normalize(null), "Normalize null → rỗng");
    }

    private static void IlisUbRulesDetectGcnFileNameCaseAndAccentInsensitive()
    {
        var rules = new GcnFolderRules { GcnKeyword = "GCN" };
        AssertTrue(rules.IsGcnFileName("GCN-719822"), "IsGcnFileName hoa");
        AssertTrue(rules.IsGcnFileName("scan gcn 01"), "IsGcnFileName thường");
        AssertTrue(rules.IsGcnFileName("Bản gốc Gcn"), "IsGcnFileName có dấu ở phần khác");
        AssertFalse(rules.IsGcnFileName("don dang ky"), "IsGcnFileName không khớp");
    }

    private static void IlisUbRulesFirstMatchingGroupWins()
    {
        var rules = new GcnFolderRules
        {
            GcnKeyword = "GCN",
            Groups = new[]
            {
                new GcnFolderRuleGroup { Suffix = "GT", Keywords = new[] { "don dang ky", "hop dong" } },
                new GcnFolderRuleGroup { Suffix = "GTK", Keywords = new[] { "hop dong", "trich luc" } }
            }
        };

        // "Hợp đồng" khớp cả hai nhóm -> nhóm đứng TRƯỚC trong Groups thắng.
        var m = rules.Match("Hợp đồng chuyển nhượng");
        AssertTrue(m is not null, "Match trả kết quả cho hợp đồng");
        AssertEqual("GT", m!.Suffix, "Nhóm đầu tiên thắng khi khớp nhiều nhóm");

        AssertEqual("GTK", rules.Match("Trích lục bản đồ")!.Suffix, "Khớp nhóm GTK");
        AssertTrue(rules.Match("anh chup hien trang") is null, "Không khớp nhóm nào → null");
    }

    private static void IlisUbRulesReportKeywordIndexForOrdering()
    {
        var rules = new GcnFolderRules
        {
            GcnKeyword = "GCN",
            Groups = new[]
            {
                new GcnFolderRuleGroup { Suffix = "GT", Keywords = new[] { "don", "hop dong", "to khai" } }
            }
        };

        AssertEqual(0, rules.Match("Đơn xin cấp")!.KeywordIndex, "KeywordIndex của từ khoá đầu");
        AssertEqual(2, rules.Match("Tờ khai thuế")!.KeywordIndex, "KeywordIndex của từ khoá thứ ba");
    }

    private static void IlisUbRulesLoaderRejectsBadFiles()
    {
        var loader = new GcnFolderRulesLoader();
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var missing = Path.Combine(dir, "khong-co.json");
            AssertThrowsInvalidOperation(() => loader.Load(missing), "Thiếu file quy tắc phải ném lỗi");

            var broken = Path.Combine(dir, "hong.json");
            File.WriteAllText(broken, "{ khong phai json");
            AssertThrowsInvalidOperation(() => loader.Load(broken), "JSON sai cú pháp phải ném lỗi");

            var dupSuffix = Path.Combine(dir, "trung-suffix.json");
            File.WriteAllText(dupSuffix, "{\"GcnKeyword\":\"GCN\",\"Groups\":[{\"Suffix\":\"GT\",\"Keywords\":[]},{\"Suffix\":\"gt\",\"Keywords\":[]}]}");
            AssertThrowsInvalidOperation(() => loader.Load(dupSuffix), "Suffix trùng nhau phải ném lỗi");

            var emptySuffix = Path.Combine(dir, "suffix-rong.json");
            File.WriteAllText(emptySuffix, "{\"GcnKeyword\":\"GCN\",\"Groups\":[{\"Suffix\":\"\",\"Keywords\":[]}]}");
            AssertThrowsInvalidOperation(() => loader.Load(emptySuffix), "Suffix rỗng phải ném lỗi");

            var emptyKeyword = Path.Combine(dir, "gcn-rong.json");
            File.WriteAllText(emptyKeyword, "{\"GcnKeyword\":\"  \",\"Groups\":[]}");
            AssertThrowsInvalidOperation(() => loader.Load(emptyKeyword), "GcnKeyword rỗng phải ném lỗi");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Nếu một nhóm có Suffix trùng GcnKeyword (kể cả khác hoa/thường, khác dấu — so sánh qua
    /// GcnFolderRules.Normalize) thì file gộp "{nhãn}-{Suffix}.pdf" sẽ ĐÈ LÊN chính file GCN thật
    /// "{nhãn}-GCN.pdf" mà Export() đã copy trước đó — mất dữ liệu âm thầm. Phải chặn ngay lúc load
    /// rules, không cho Export chạy với cấu hình sai này.
    /// </summary>
    private static void IlisUbRulesLoaderRejectsSuffixMatchingGcnKeyword()
    {
        var loader = new GcnFolderRulesLoader();
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-rules-suffix-gcn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // "gcn" khác hoa/thường với GcnKeyword "GCN" nhưng Normalize phải coi là trùng.
            var path = Path.Combine(dir, "suffix-trung-gcn.json");
            File.WriteAllText(path, "{\"GcnKeyword\":\"GCN\",\"Groups\":[{\"Suffix\":\"gcn\",\"Keywords\":[]}]}");
            AssertThrowsInvalidOperation(() => loader.Load(path),
                "Suffix trùng GcnKeyword (không phân biệt hoa/thường) phải ném lỗi, không cho đè lên file GCN thật");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void IlisUbRulesLoaderReadsShippedDefaultFile()
    {
        // File giao hàng: Keywords rỗng (chủ dự án tự điền), nhưng cấu trúc phải hợp lệ và load được.
        // FindRepositoryFile là helper SẴN CÓ trong Program.cs (dòng 492) — leo ngược cây thư mục từ
        // cwd/BaseDirectory, không phụ thuộc RuntimeIdentifier trong đường dẫn output.
        var path = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR.Business", "IlisUb", "gcn-ilis-ub-rules.json"));

        var rules = new GcnFolderRulesLoader().Load(path);
        AssertEqual("GCN", rules.GcnKeyword, "GcnKeyword mặc định");
        AssertEqual(2, rules.Groups.Count, "Số nhóm mặc định");
        AssertEqual("GT", rules.Groups[0].Suffix, "Nhóm đầu là GT");
        AssertEqual("GTK", rules.Groups[1].Suffix, "Nhóm sau là GTK");
    }

    private static void PdfMergerKeepsGivenOrderAndCountsPages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.pdf");
            var b = Path.Combine(dir, "b.pdf");
            CreatePdf(a, 2);
            CreatePdf(b, 3);

            var outPath = Path.Combine(dir, "out.pdf");
            var result = PdfMerger.Merge(new[] { a, b }, outPath);

            AssertEqual(2, result.MergedFiles, "Số file đã gộp");
            AssertEqual(0, result.SkippedFiles, "Số file bị bỏ qua");
            AssertEqual(5, result.Pages, "Tổng số trang sau gộp");
            AssertTrue(File.Exists(outPath), "File gộp được tạo ra");
            AssertEqual(5, CountPages(outPath), "Số trang đọc lại từ file gộp");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void PdfMergerSkipsCorruptFilesWithoutFailingWholeMerge()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-merge-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "good.pdf");
            var bad = Path.Combine(dir, "bad.pdf");
            CreatePdf(good, 1);
            File.WriteAllText(bad, "day khong phai pdf");

            var errors = new List<string>();
            var outPath = Path.Combine(dir, "out.pdf");
            var result = PdfMerger.Merge(new[] { bad, good }, outPath, (path, _) => errors.Add(Path.GetFileName(path)));

            AssertEqual(1, result.MergedFiles, "Chỉ file hợp lệ được gộp");
            AssertEqual(1, result.SkippedFiles, "File hỏng bị bỏ qua");
            AssertEqual(1, errors.Count, "Callback lỗi được gọi đúng một lần");
            AssertEqual("bad.pdf", errors[0], "Callback lỗi báo đúng tên file");
            AssertTrue(File.Exists(outPath), "Vẫn tạo file gộp từ phần còn lại");

            // Không gộp được gì thì KHÔNG tạo file rỗng.
            var emptyOut = Path.Combine(dir, "empty.pdf");
            var emptyResult = PdfMerger.Merge(new[] { bad }, emptyOut);
            AssertEqual(0, emptyResult.MergedFiles, "Không file nào gộp được");
            AssertFalse(File.Exists(emptyOut), "Không tạo file gộp rỗng");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void PdfMergerMergesPagesWithRealContentAndStaysReadable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-merge-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.pdf");
            var b = Path.Combine(dir, "b.pdf");
            CreatePdfWithContent(a, 2);
            CreatePdfWithContent(b, 3);

            var outPath = Path.Combine(dir, "out.pdf");
            var result = PdfMerger.Merge(new[] { a, b }, outPath);

            AssertEqual(2, result.MergedFiles, "Số file có nội dung thật đã gộp");
            AssertEqual(0, result.SkippedFiles, "Không file nào bị bỏ qua");
            AssertEqual(5, result.Pages, "Tổng số trang sau gộp (có nội dung thật)");
            AssertTrue(File.Exists(outPath), "File gộp có nội dung được tạo ra");

            // Mở lại để chắc file không bị hỏng: nếu dispose document nguồn quá sớm (trước Save),
            // PdfSharp có thể ghi thiếu nội dung khiến file gộp không đọc lại được hoặc thiếu trang.
            using var reopened = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
            AssertEqual(5, reopened.PageCount, "Số trang đọc lại từ file gộp có nội dung thật");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void PdfMergerRollsBackPartiallyAddedPagesOfFailingFile()
    {
        // Dựng một file PDF thật "PdfReader.Open thành công nhưng hỏng đúng giữa trang" một cách đáng
        // tin cậy bằng PdfSharp là KHÔNG khả thi trong thời gian hợp lý: đã thử hỏng /Kids trong cây
        // trang (PdfSharp ném lỗi ngay ở trang ĐẦU TIÊN khi dựng lại toàn bộ cây, không phải giữa
        // file), hỏng /Contents hoặc /MediaBox của một trang giữa (PdfSharp tự bỏ qua lỗi hoặc báo
        // lỗi ngay từ PdfReader.Open chứ không lọt qua Open thành công), và lệch /Count so với /Kids
        // thật (PdfSharp tự cập nhật lại PageCount, không ném lỗi). Vì vậy test này gọi trực tiếp hàm
        // nội bộ AppendPagesWithRollback — ĐÚNG đường code mà Merge() dùng thật — với một "getPage"
        // giả lập lỗi ở trang thứ hai của file, để kiểm chứng hành vi rollback atomic theo từng file.
        var dir = Path.Combine(Path.GetTempPath(), "ilisub-merge-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var priorFile = Path.Combine(dir, "prior.pdf");
            var failingFile = Path.Combine(dir, "failing.pdf");
            CreatePdf(priorFile, 1);
            CreatePdf(failingFile, 2);

            using var target = new PdfDocument();

            // Giả lập: đã gộp thành công 1 file trước đó -> target có sẵn 1 trang.
            using (var priorSource = PdfReader.Open(priorFile, PdfDocumentOpenMode.Import))
            {
                PdfMerger.AppendPagesWithRollback(target, priorSource.PageCount, i => priorSource.Pages[i]);
            }
            AssertEqual(1, target.PageCount, "Trang của file trước đã gộp thành công");

            using var failingSource = PdfReader.Open(failingFile, PdfDocumentOpenMode.Import);
            var threwExpectedException = false;
            try
            {
                PdfMerger.AppendPagesWithRollback(target, failingSource.PageCount, i =>
                    i == 0
                        ? failingSource.Pages[0]
                        : throw new InvalidOperationException("Giả lập lỗi ở trang thứ hai của file"));
            }
            catch (InvalidOperationException)
            {
                threwExpectedException = true;
            }

            AssertTrue(threwExpectedException, "AppendPagesWithRollback phải ném lại lỗi gốc");
            AssertEqual(1, target.PageCount, "Trang lỡ thêm của file hỏng bị gỡ hết, chỉ còn trang của file trước đó");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void CreatePdfWithContent(string path, int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Trang noi dung {i}", new XFont("Arial", 20), XBrushes.Black, new XPoint(20, 40));
            gfx.DrawRectangle(XBrushes.Red, 10, 60, 100, 40);
        }
        doc.Save(path);
    }

    /// <summary>
    /// Tạo PDF mà MỖI TRANG có chiều rộng riêng biệt (đơn vị point) — dùng để kiểm tra THỨ TỰ THẬT
    /// của trang sau khi gộp (đọc lại bằng <see cref="ReadPageWidths"/>). Trang trắng tạo bởi
    /// <c>CreatePdf</c> giống hệt nhau nên không phân biệt được thứ tự, chỉ đếm được tổng số trang.
    /// </summary>
    private static void CreatePdfWithWidths(string path, params double[] widthsPt)
    {
        using var doc = new PdfDocument();
        foreach (var w in widthsPt)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(w);
            page.Height = XUnit.FromPoint(200);
        }
        doc.Save(path);
    }

    /// <summary>Đọc lại chiều rộng (point, làm tròn) của từng trang theo đúng thứ tự trong file.</summary>
    private static List<string> ReadPageWidths(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var widths = new List<string>();
        for (int i = 0; i < doc.PageCount; i++)
            widths.Add(Math.Round(doc.Pages[i].Width.Point).ToString());
        return widths;
    }

    private static void AssertThrowsInvalidOperation(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("FAIL: " + message);
    }

    /// <summary>Dựng một thư mục nguồn tạm cho test, trả về đường dẫn gốc.</summary>
    private static string NewTempTree(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ilisub-{tag}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Danh sách "file đã quét" rỗng — dùng cho các test KHÔNG mô phỏng việc đổi <c>GcnKeyword</c> giữa
    /// lúc quét và lúc Export; khi đó việc loại file GCN khỏi tập kèm theo chỉ dựa vào bộ quy tắc,
    /// đúng như hành vi trước đây.
    /// </summary>
    private static readonly string[] NoScannedGcnPaths = Array.Empty<string>();

    /// <summary>Tập file đã quét dùng trong CopyAttachments/ContainsGcnFile (so khớp theo đường dẫn đầy đủ).</summary>
    private static HashSet<string> ScannedSet(params string[] paths)
        => new(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

    private static GcnFolderRules TestRules() => new()
    {
        GcnKeyword = "GCN",
        Groups = new[]
        {
            new GcnFolderRuleGroup { Suffix = "GT", Keywords = new[] { "don dang ky", "hop dong" } },
            new GcnFolderRuleGroup { Suffix = "GTK", Keywords = new[] { "trich luc" } }
        }
    };

    private static void TreeExporterCreatesOneFolderPerGcnAsSiblings()
    {
        var work = NewTempTree("tree-multi");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);
            var gcnA = Path.Combine(hoSo, "GCN-abc.pdf");
            var gcnB = Path.Combine(hoSo, "GCN-xyz.pdf");
            CreatePdf(gcnA, 1);
            CreatePdf(gcnB, 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnA, "A"), new GcnTreeSource(gcnB, "B") },
                TestRules(), NoScannedGcnPaths));

            AssertEqual(Path.Combine(dest, "Cha"), result.RootPath, "Thư mục gốc cây đích");
            AssertEqual(1, result.HoSoFolders, "Số thư mục hồ sơ");
            AssertEqual(2, result.LabelFolders, "Số thư mục nhãn");
            AssertEqual(2, result.GcnFilesCopied, "Số file GCN đã copy");

            // Thư mục lá HoSo01 bị THAY THẾ bằng hai thư mục nhãn ngang hàng.
            AssertFalse(Directory.Exists(Path.Combine(dest, "Cha", "HoSo01")), "Không giữ lại tầng HoSo01");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "A-GCN.pdf")), "A-GCN.pdf");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "B", "B-GCN.pdf")), "B-GCN.pdf");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterSuffixesDuplicateSerialAndNamesFilesAfterFolder()
    {
        var work = NewTempTree("tree-dup");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);
            var g1 = Path.Combine(hoSo, "GCN-01.pdf");
            var g2 = Path.Combine(hoSo, "GCN-02.pdf");
            CreatePdf(g1, 1);
            CreatePdf(g2, 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(g1, "719822"), new GcnTreeSource(g2, "719822") },
                TestRules(), NoScannedGcnPaths));

            AssertEqual(2, result.LabelFolders, "Hai thư mục nhãn cho hai GCN trùng serial");
            // Duyệt file GCN theo tên A→Z: GCN-01 giữ serial trần, GCN-02 nhận hậu tố _2.
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "719822", "719822-GCN.pdf")), "Thư mục serial trần");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "719822_2", "719822_2-GCN.pdf")), "Tên file theo TÊN THƯ MỤC");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterFallsBackToSourceFolderNameWhenSerialMissing()
    {
        var work = NewTempTree("tree-noserial");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo09");
            Directory.CreateDirectory(hoSo);
            var g = Path.Combine(hoSo, "GCN-01.pdf");
            CreatePdf(g, 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(g, "   ") },
                TestRules(), NoScannedGcnPaths));

            AssertEqual(1, result.MissingSerialFolders, "Đếm thư mục thiếu serial");
            AssertTrue(result.Warnings.Count > 0, "Có cảnh báo thiếu serial");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "HoSo09", "HoSo09-GCN.pdf")), "Dùng tên thư mục nguồn làm nhãn");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterSkipsFoldersWithoutSuccessfulGcn()
    {
        var work = NewTempTree("tree-skip");
        try
        {
            var src = Path.Combine(work, "Cha");
            var coGcn = Path.Combine(src, "HoSoOk");
            var khongGcn = Path.Combine(src, "HoSoLoi");
            Directory.CreateDirectory(coGcn);
            Directory.CreateDirectory(khongGcn);
            var ok = Path.Combine(coGcn, "GCN-01.pdf");
            CreatePdf(ok, 1);
            CreatePdf(Path.Combine(khongGcn, "GCN-99.pdf"), 1);   // OCR lỗi → KHÔNG nằm trong Sources
            CreatePdf(Path.Combine(khongGcn, "don dang ky.pdf"), 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest, new[] { new GcnTreeSource(ok, "A") }, TestRules(), NoScannedGcnPaths));

            AssertTrue(Directory.Exists(Path.Combine(dest, "Cha", "A")), "Thư mục có GCN thành công được dựng");
            AssertFalse(Directory.Exists(Path.Combine(dest, "Cha", "HoSoLoi")), "Thư mục không có GCN thành công bị bỏ qua");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterSuffixesRootWhenDestinationAlreadyExists()
    {
        var work = NewTempTree("tree-root");
        try
        {
            var src = Path.Combine(work, "Cha");
            Directory.CreateDirectory(src);
            var g = Path.Combine(src, "GCN-01.pdf");
            CreatePdf(g, 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(Path.Combine(dest, "Cha"));   // lần export trước đã có

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest, new[] { new GcnTreeSource(g, "A") }, TestRules(), NoScannedGcnPaths));

            AssertEqual(Path.Combine(dest, "Cha_2"), result.RootPath, "Root trùng tên thì thêm hậu tố _2");
            // Thư mục nguồn CHÍNH LÀ thư mục hồ sơ (rel rỗng) → thư mục nhãn nằm ngay dưới root.
            AssertTrue(File.Exists(Path.Combine(dest, "Cha_2", "A", "A-GCN.pdf")), "Thư mục nhãn nằm ngay dưới root");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// Khoá bất biến chống trùng nhãn: hai thư mục HỒ SƠ KHÁC NHAU (H1, H2), khi ánh xạ về CÙNG một
    /// thư mục cha đích, vẫn không được ghi đè lẫn nhau dù mỗi thư mục hồ sơ tự khởi tạo "usedLabels"
    /// riêng. Việc chống trùng ở đây trông cậy vào Directory.Exists(destParent, candidate) trong
    /// AllocateLabel — nếu sau này ai đó bỏ nhánh Directory.Exists vì tưởng usedLabels-trong-nhóm là
    /// đủ, test này phải FAIL ngay.
    /// </summary>
    private static void TreeExporterSuffixesDuplicateSerialAcrossDifferentHoSoFolders()
    {
        var work = NewTempTree("tree-cross-dup");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo1 = Path.Combine(src, "H1");
            var hoSo2 = Path.Combine(src, "H2");
            Directory.CreateDirectory(hoSo1);
            Directory.CreateDirectory(hoSo2);
            var g1 = Path.Combine(hoSo1, "GCN-01.pdf");
            var g2 = Path.Combine(hoSo2, "GCN-01.pdf");
            CreatePdf(g1, 1);
            CreatePdf(g2, 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            // H1 và H2 đều là con TRỰC TIẾP của Cha → cả hai ánh xạ về cùng destParent = rootPath.
            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(g1, "719822"), new GcnTreeSource(g2, "719822") },
                TestRules(), NoScannedGcnPaths));

            AssertEqual(2, result.HoSoFolders, "Hai thư mục hồ sơ khác nhau đều được dựng");
            AssertEqual(2, result.LabelFolders, "Hai thư mục nhãn cho hai GCN cùng serial khác hồ sơ");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "719822", "719822-GCN.pdf")), "Thư mục serial trần (hồ sơ xử lý trước theo thứ tự đường dẫn)");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "719822_2", "719822_2-GCN.pdf")), "Thư mục hồ sơ sau nhận hậu tố _2 dù usedLabels của nó rỗng");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterMergesAttachmentsByRuleOrderIntoEveryLabelFolder()
    {
        var work = NewTempTree("tree-merge");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);
            var gcnA = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnB = Path.Combine(hoSo, "GCN-b.pdf");
            CreatePdf(gcnA, 1);
            CreatePdf(gcnB, 1);
            // Rules: GT = ["don dang ky", "hop dong"], GTK = ["trich luc"].
            // Mỗi trang có CHIỀU RỘNG riêng biệt (không phải trang trắng giống hệt nhau như CreatePdf)
            // để kiểm tra được THỨ TỰ THẬT của trang sau khi gộp, không chỉ tổng số trang — nếu chỉ
            // đếm tổng thì gộp "Đơn đăng ký" trước hay "Hợp đồng" trước đều ra cùng một con số.
            CreatePdfWithWidths(Path.Combine(hoSo, "Hợp đồng.pdf"), 210, 211, 212);        // GT, từ khoá index 1 — 3 trang
            CreatePdfWithWidths(Path.Combine(hoSo, "Đơn đăng ký.pdf"), 110, 111);          // GT, từ khoá index 0 — 2 trang
            CreatePdfWithWidths(Path.Combine(hoSo, "Trích lục.pdf"), 310, 311, 312, 313);  // GTK — 4 trang

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnA, "A"), new GcnTreeSource(gcnB, "B") },
                TestRules(), NoScannedGcnPaths));

            // Gộp tính MỘT LẦN cho thư mục hồ sơ: 2 file vào GT + 1 file vào GTK = 3.
            AssertEqual(3, result.MergedFiles, "Số file đã gộp");

            // Thứ tự trang MONG ĐỢI = thứ tự TỪ KHOÁ trong Keywords: "don dang ky" (index 0, 2 trang,
            // rộng 110/111) rồi "hop dong" (index 1, 3 trang, rộng 210/211/212).
            var expectedGtWidths = "110,111,210,211,212";
            var expectedGtkWidths = "310,311,312,313";

            foreach (var label in new[] { "A", "B" })
            {
                var gt = Path.Combine(dest, "Cha", label, $"{label}-GT.pdf");
                var gtk = Path.Combine(dest, "Cha", label, $"{label}-GTK.pdf");
                AssertTrue(File.Exists(gt), $"{label}-GT.pdf tồn tại");
                AssertTrue(File.Exists(gtk), $"{label}-GTK.pdf tồn tại");
                AssertEqual(5, CountPages(gt), $"{label}-GT.pdf gộp đủ 5 trang");
                AssertEqual(4, CountPages(gtk), $"{label}-GTK.pdf gộp đủ 4 trang");
                // Kiểm tra THỨ TỰ THẬT bằng chiều rộng từng trang, không chỉ tổng số trang.
                AssertEqual(expectedGtWidths, string.Join(",", ReadPageWidths(gt)), $"{label}-GT.pdf đúng thứ tự trang theo từ khoá");
                AssertEqual(expectedGtkWidths, string.Join(",", ReadPageWidths(gtk)), $"{label}-GTK.pdf đúng thứ tự trang");
            }

            // File kèm theo gốc KHÔNG được copy lẻ khi đã vào nhóm gộp.
            AssertFalse(File.Exists(Path.Combine(dest, "Cha", "A", "Hợp đồng.pdf")), "Không copy lẻ file đã gộp");
            // File GCN của serial khác KHÔNG lọt vào thư mục nhãn này.
            AssertFalse(File.Exists(Path.Combine(dest, "Cha", "A", "GCN-b.pdf")), "Không copy GCN của serial khác");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterCopiesUnmatchedAndNonPdfFilesAsIs()
    {
        var work = NewTempTree("tree-asis");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);
            var gcn = Path.Combine(hoSo, "GCN-a.pdf");
            CreatePdf(gcn, 1);
            CreatePdf(Path.Combine(hoSo, "bien ban khong co trong rule.pdf"), 1);
            File.WriteAllText(Path.Combine(hoSo, "ghi chu.txt"), "noi dung");

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest, new[] { new GcnTreeSource(gcn, "A") }, TestRules(), NoScannedGcnPaths));

            AssertEqual(0, result.MergedFiles, "Không có file nào khớp rule");
            AssertEqual(2, result.CopiedAsIsFiles, "Hai file copy nguyên tên");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "bien ban khong co trong rule.pdf")), "PDF không khớp rule giữ nguyên tên");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "ghi chu.txt")), "File không phải PDF giữ nguyên tên");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterCopiesSubFolderWithoutGcnAndSkipsSubFolderWithGcn()
    {
        var work = NewTempTree("tree-sub");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            var phuLuc = Path.Combine(hoSo, "PhuLuc");          // không có GCN → copy nguyên
            var hoSoCon = Path.Combine(hoSo, "HoSoCon");        // CÓ GCN → hồ sơ độc lập
            Directory.CreateDirectory(phuLuc);
            Directory.CreateDirectory(hoSoCon);

            var gcnCha = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnCon = Path.Combine(hoSoCon, "GCN-c.pdf");
            CreatePdf(gcnCha, 1);
            CreatePdf(gcnCon, 1);
            File.WriteAllText(Path.Combine(phuLuc, "phu luc.txt"), "x");

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnCha, "A"), new GcnTreeSource(gcnCon, "C") },
                TestRules(), NoScannedGcnPaths));

            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "PhuLuc", "phu luc.txt")), "Thư mục con không có GCN được copy nguyên");
            AssertFalse(Directory.Exists(Path.Combine(dest, "Cha", "A", "HoSoCon")), "Thư mục con có GCN không bị copy lồng");
            // HoSoCon là thư mục hồ sơ độc lập: cha đích của nó là ánh xạ của HoSo01.
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "HoSo01", "C", "C-GCN.pdf")), "Thư mục con có GCN xử lý ở nhánh riêng");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// C1 — bất biến CỐT LÕI: không file GCN nào được lọt vào thư mục nhãn của một GCN khác.
    ///
    /// Kịch bản phá: thư mục hồ sơ "HoSo01" vừa THIẾU SERIAL (nhãn dự phòng = tên thư mục "HoSo01")
    /// vừa CHỨA một hồ sơ con "HoSoCon". Thư mục nhãn của GCN cha khi đó là "dest\Cha\HoSo01", còn
    /// destParent của hồ sơ con cũng là "dest\Cha\HoSo01" — TRÙNG NHAU TUYỆT ĐỐI. Vì byFolder sắp theo
    /// đường dẫn nên hồ sơ cha chạy trước và cấp nhãn "HoSo01" khi thư mục đó còn chưa tồn tại
    /// (Directory.Exists không thấy gì), rồi lượt sau ghi thẳng "C\C-GCN.pdf" vào bên trong nó.
    ///
    /// Phòng thủ là tập đường dẫn dành riêng dựng TRƯỚC vòng lặp (BuildReservedDestinationPaths).
    /// </summary>
    private static void TreeExporterNeverPutsChildHoSoGcnInsideParentLabelFolder()
    {
        var work = NewTempTree("tree-nested-noserial");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            var hoSoCon = Path.Combine(hoSo, "HoSoCon");
            Directory.CreateDirectory(hoSoCon);

            var gcnCha = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnCon = Path.Combine(hoSoCon, "GCN-c.pdf");
            CreatePdf(gcnCha, 1);
            CreatePdf(gcnCon, 1);
            CreatePdf(Path.Combine(hoSo, "don.pdf"), 1);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnCha, "   "), new GcnTreeSource(gcnCon, "C") },
                TestRules(), NoScannedGcnPaths));

            AssertEqual(2, result.HoSoFolders, "Cả hai thư mục hồ sơ đều được dựng");
            AssertEqual(1, result.MissingSerialFolders, "Đúng một nhãn phải dùng tên thư mục gốc");

            // Triệu chứng trực tiếp của lỗi: nếu nhãn cha bị cấp trùng "HoSo01" thì chính file GCN của
            // hồ sơ CON (Cha\HoSo01\C\C-GCN.pdf) nằm ngay bên trong thư mục nhãn của GCN cha.
            AssertFalse(File.Exists(Path.Combine(dest, "Cha", "HoSo01", "HoSo01-GCN.pdf")),
                "Thư mục nhãn của GCN cha không được chiếm đường dẫn đã dành riêng cho cây hồ sơ con");

            // Nhãn của GCN cha KHÔNG được là "HoSo01" (đường dẫn đó đã dành riêng cho cây hồ sơ con).
            var parentLabelDir = Path.Combine(dest, "Cha", "HoSo01_2");
            AssertTrue(File.Exists(Path.Combine(parentLabelDir, "HoSo01_2-GCN.pdf")),
                "GCN cha thiếu serial phải nhận nhãn KHÁC tên thư mục đang bị hồ sơ con chiếm chỗ");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "HoSo01", "C", "C-GCN.pdf")),
                "GCN của hồ sơ con nằm đúng nhánh riêng của nó");

            // Bất biến: KHÔNG file GCN nào của hồ sơ con lọt vào cây thư mục nhãn của GCN cha.
            var leaked = Directory.EnumerateFiles(parentLabelDir, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Contains("C-GCN", StringComparison.OrdinalIgnoreCase)
                            || Path.GetFileName(p).Equals("GCN-c.pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();
            AssertEqual(0, leaked.Count,
                "Không file GCN nào của hồ sơ con được nằm trong thư mục nhãn của GCN cha");
            AssertFalse(Directory.Exists(Path.Combine(parentLabelDir, "HoSoCon")),
                "Thư mục hồ sơ con không bị copy lồng vào thư mục nhãn của cha");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// I2 — người dùng đổi <c>GcnKeyword</c> trong file JSON GIỮA lúc quét và lúc Export (app khuyến
    /// khích sửa file rồi Export lại là ăn ngay). Bộ lọc đầu vào đã chạy với từ khoá CŨ, còn rules ở
    /// bước Export là từ khoá MỚI ⇒ file GCN không còn khớp <c>IsGcnFileName</c> và rơi vào tập "file
    /// kèm theo", bị gộp vào <c>{nhãn}-GT.pdf</c>. Vi phạm spec §5.5.
    ///
    /// Ở đây rules lúc Export dùng <c>GcnKeyword = "GIAY CHUNG NHAN"</c> (không khớp tên "GCN-a"/"GCN-b")
    /// và nhóm GT lại có từ khoá "gcn" nên hai file GCN CHẮC CHẮN sẽ bị gộp nếu thiếu phòng thủ.
    /// </summary>
    private static void TreeExporterKeepsScannedGcnOutOfAttachmentsWhenKeywordChanged()
    {
        var work = NewTempTree("tree-doi-keyword");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);

            var gcnA = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnLoi = Path.Combine(hoSo, "GCN-b.pdf");     // đã quét nhưng OCR LỖI → không có trong Sources
            CreatePdf(gcnA, 1);
            CreatePdf(gcnLoi, 1);
            CreatePdfWithWidths(Path.Combine(hoSo, "don dang ky.pdf"), 110, 111);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var rulesMoi = new GcnFolderRules
            {
                GcnKeyword = "GIAY CHUNG NHAN",
                Groups = new[]
                {
                    new GcnFolderRuleGroup { Suffix = "GT", Keywords = new[] { "don dang ky", "gcn" } }
                }
            };

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnA, "A") },
                rulesMoi,
                new[] { gcnA, gcnLoi }));   // ViewModel truyền TOÀN BỘ file đã quét, kể cả file lỗi

            var labelDir = Path.Combine(dest, "Cha", "A");
            AssertTrue(File.Exists(Path.Combine(labelDir, "A-GCN.pdf")), "File GCN của nhãn vẫn được copy");
            AssertEqual(1, result.MergedFiles, "Chỉ đúng file kèm theo THẬT được gộp");
            AssertEqual(0, result.CopiedAsIsFiles, "Không file GCN nào bị copy lẻ vào thư mục nhãn");

            // Nếu hai file GCN lọt vào nhóm gộp thì A-GT.pdf sẽ có 4 trang (2 + 1 + 1) thay vì 2.
            var gt = Path.Combine(labelDir, "A-GT.pdf");
            AssertTrue(File.Exists(gt), "A-GT.pdf vẫn được tạo từ file kèm theo thật");
            AssertEqual("110,111", string.Join(",", ReadPageWidths(gt)),
                "A-GT.pdf chỉ chứa trang của \"don dang ky.pdf\" — không file GCN nào bị gộp vào");

            AssertFalse(File.Exists(Path.Combine(labelDir, "GCN-b.pdf")), "File GCN OCR lỗi không bị copy lẻ");
            AssertFalse(File.Exists(Path.Combine(labelDir, "GCN-a.pdf")), "File GCN của chính nhãn không bị copy lẻ");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// I3 — thư mục con bị loại khỏi diện copy-nguyên vì <c>ContainsGcnFile</c> trả true nhưng KHÔNG bao
    /// giờ trở thành thư mục hồ sơ (không GCN nào trong cây con của nó OCR thành công) thì trước đây
    /// biến mất khỏi cây đích mà không một cảnh báo nào.
    ///
    /// Rất thực tế với dữ liệu Uông Bí: <c>HoSo01\Anh\GCN-scan.jpg</c> — ảnh bị bộ lọc đầu vào loại
    /// (màn chỉ nhận .pdf) nên không bao giờ có envelope, nhưng tên vẫn chứa "GCN".
    /// </summary>
    private static void TreeExporterWarnsWhenSkippingSubFolderThatNeverBecomesHoSo()
    {
        var work = NewTempTree("tree-sub-canh-bao");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            var anh = Path.Combine(hoSo, "Anh");            // có tên GCN nhưng KHÔNG bao giờ là thư mục hồ sơ
            var phuLuc = Path.Combine(hoSo, "PhuLuc");      // không dính GCN → copy nguyên, không cảnh báo
            var hoSoCon = Path.Combine(hoSo, "HoSoCon");    // LÀ thư mục hồ sơ → bỏ qua ĐÚNG, không cảnh báo
            Directory.CreateDirectory(anh);
            Directory.CreateDirectory(phuLuc);
            Directory.CreateDirectory(hoSoCon);

            var gcnCha = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnCon = Path.Combine(hoSoCon, "GCN-c.pdf");
            CreatePdf(gcnCha, 1);
            CreatePdf(gcnCon, 1);
            File.WriteAllText(Path.Combine(anh, "GCN-scan.jpg"), "gia lap anh scan");
            File.WriteAllText(Path.Combine(phuLuc, "phu luc.txt"), "x");

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnCha, "A"), new GcnTreeSource(gcnCon, "C") },
                TestRules(),
                new[] { gcnCha, gcnCon }));

            AssertFalse(Directory.Exists(Path.Combine(dest, "Cha", "A", "Anh")),
                "Thư mục chứa file mang tên GCN vẫn không được copy vào thư mục nhãn");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "PhuLuc", "phu luc.txt")),
                "Thư mục con sạch vẫn được copy nguyên");
            AssertFalse(Directory.Exists(Path.Combine(dest, "Cha", "A", "HoSoCon")),
                "Thư mục hồ sơ con không bị copy lồng");

            AssertEqual(1, result.Warnings.Count,
                "Đúng MỘT cảnh báo: chỉ thư mục bị bỏ qua mà không thành thư mục hồ sơ mới cần cảnh báo");
            AssertTrue(result.Warnings[0].Contains("Anh", StringComparison.Ordinal),
                "Cảnh báo phải nêu rõ đường dẫn thư mục bị bỏ qua");
            AssertTrue(result.Warnings[0].Contains("KHÔNG có GCN nào đọc thành công", StringComparison.Ordinal),
                "Cảnh báo phải nêu rõ lý do bỏ qua");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// M3 — chọn nguồn là GỐC Ổ ĐĨA. <c>TrimEnd</c> biến "E:\" thành "E:" mà Windows hiểu là *thư mục
    /// hiện hành của ổ E*, khiến <c>Path.GetRelativePath</c> trả đường dẫn lệch tầng; đồng thời
    /// <c>Path.GetFileName("E:")</c> trả rỗng nên thư mục gốc cây đích rơi vào tên "output".
    /// Không dựng được ổ đĩa thật trong CI nên test thẳng hai hàm tách riêng.
    /// </summary>
    private static void TreeExporterKeepsDriveRootSourcePathIntact()
    {
        var work = NewTempTree("tree-drive-root");
        try
        {
            var driveRoot = Path.GetPathRoot(work)!;                    // ví dụ "D:\"
            AssertEqual(driveRoot, GcnTreeExporter.NormalizeSourceRoot(driveRoot),
                "Gốc ổ đĩa phải giữ nguyên dấu phân cách cuối");
            AssertEqual(driveRoot.TrimEnd('\\', '/').TrimEnd(':'), GcnTreeExporter.RootFolderName(driveRoot),
                "Thư mục gốc cây đích lấy theo ký tự ổ đĩa, không phải \"output\"");

            var normal = Path.Combine(work, "Cha");
            AssertEqual(normal, GcnTreeExporter.NormalizeSourceRoot(normal + Path.DirectorySeparatorChar),
                "Thư mục thường vẫn bị cắt dấu phân cách cuối");
            AssertEqual("Cha", GcnTreeExporter.RootFolderName(GcnTreeExporter.NormalizeSourceRoot(normal)),
                "Thư mục thường lấy đúng tên lá");

            // Với gốc ổ đĩa, đường dẫn tương đối phải tính từ "E:\" chứ không phải "E:".
            AssertEqual(
                Path.GetRelativePath(driveRoot, Path.Combine(driveRoot, "A", "B")),
                Path.GetRelativePath(GcnTreeExporter.NormalizeSourceRoot(driveRoot), Path.Combine(driveRoot, "A", "B")),
                "Đường dẫn tương đối tính từ gốc ổ đĩa không bị lệch tầng");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// M4 — một lỗi I/O lẻ khi ghi file kèm theo (ở đây: thư mục con trùng tên với file gộp
    /// <c>A-GT.pdf</c> sinh ra trong thư mục nhãn A ⇒ <c>Directory.CreateDirectory</c> ném IOException)
    /// trước đây thoát khỏi cả <c>CopyAttachments</c> và bị bắt ở tầng Export ⇒ MẤT TOÀN BỘ file kèm
    /// theo của mọi thư mục nhãn còn lại, không chỉ đúng cái tên gây lỗi.
    /// </summary>
    private static void TreeExporterLimitsAttachmentFailureToOneLabelFolder()
    {
        var work = NewTempTree("tree-loi-mot-nhan");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            var trungTen = Path.Combine(hoSo, "A-GT.pdf");   // THƯ MỤC trùng tên file gộp của nhãn A
            Directory.CreateDirectory(trungTen);

            var gcnA = Path.Combine(hoSo, "GCN-a.pdf");
            var gcnB = Path.Combine(hoSo, "GCN-b.pdf");
            CreatePdf(gcnA, 1);
            CreatePdf(gcnB, 1);
            CreatePdfWithWidths(Path.Combine(hoSo, "don dang ky.pdf"), 110, 111);
            File.WriteAllText(Path.Combine(trungTen, "noi dung.txt"), "x");

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest,
                new[] { new GcnTreeSource(gcnA, "A"), new GcnTreeSource(gcnB, "B") },
                TestRules(),
                new[] { gcnA, gcnB }));

            AssertEqual(1, result.HoSoFolders, "Lỗi lẻ không được huỷ cả thư mục hồ sơ");

            // Nhãn B nằm SAU nhãn A trong vòng lặp: trước khi sửa, lỗi của A cuốn theo toàn bộ phần B.
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "B", "B-GT.pdf")),
                "Thư mục nhãn còn lại vẫn nhận đủ file gộp");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "B", "A-GT.pdf", "noi dung.txt")),
                "Thư mục nhãn còn lại vẫn nhận đủ thư mục con copy nguyên");

            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "A-GT.pdf")),
                "Phần đã ghi được của thư mục nhãn lỗi vẫn giữ nguyên");
            AssertEqual(1, result.Warnings.Count, "Đúng một cảnh báo cho đúng thư mục nhãn bị lỗi");
            AssertTrue(result.Warnings[0].Contains("thư mục nhãn", StringComparison.Ordinal),
                "Cảnh báo nêu rõ đây là lỗi ghi file kèm theo của một thư mục nhãn");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void TreeExporterCreatesNoMergedFileWhenKeywordsAreEmpty()
    {
        var work = NewTempTree("tree-empty-rules");
        try
        {
            var src = Path.Combine(work, "Cha");
            var hoSo = Path.Combine(src, "HoSo01");
            Directory.CreateDirectory(hoSo);
            var gcn = Path.Combine(hoSo, "GCN-a.pdf");
            CreatePdf(gcn, 1);
            CreatePdf(Path.Combine(hoSo, "Đơn đăng ký.pdf"), 2);

            var dest = Path.Combine(work, "Dest");
            Directory.CreateDirectory(dest);

            // Đúng cấu hình GIAO HÀNG: Keywords rỗng.
            var rules = new GcnFolderRules
            {
                GcnKeyword = "GCN",
                Groups = new[]
                {
                    new GcnFolderRuleGroup { Suffix = "GT", Keywords = Array.Empty<string>() },
                    new GcnFolderRuleGroup { Suffix = "GTK", Keywords = Array.Empty<string>() }
                }
            };

            var result = new GcnTreeExporter().Export(new GcnTreeExportRequest(
                src, dest, new[] { new GcnTreeSource(gcn, "A") }, rules, NoScannedGcnPaths));

            AssertEqual(0, result.MergedFiles, "Keywords rỗng → không gộp file nào");
            AssertEqual(1, result.CopiedAsIsFiles, "File kèm theo copy nguyên tên");
            AssertFalse(File.Exists(Path.Combine(dest, "Cha", "A", "A-GT.pdf")), "Không tạo A-GT.pdf rỗng");
            AssertTrue(File.Exists(Path.Combine(dest, "Cha", "A", "Đơn đăng ký.pdf")), "Giữ nguyên tên file kèm theo");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>
    /// Fail-CLOSED khi không đọc được thư mục con: nếu enumerate ném lỗi (mất quyền đọc, ổ đĩa rớt
    /// kết nối, ...) TRƯỚC khi chạm tới file GCN nằm sâu bên trong, hàm KHÔNG được coi thư mục đó là
    /// "an toàn, không có GCN" — vì làm vậy sẽ khiến CopyDirectory copy nhầm dữ liệu của một GCN khác
    /// vào thư mục nhãn hiện tại (chính bất biến mà toàn bộ Task 4 tồn tại để bảo vệ).
    ///
    /// Không dùng ACL thật trên đĩa (icacls /deny) để mô phỏng: đã thử trên môi trường sandbox này —
    /// deny ACE áp cho tài khoản hiện tại KHÔNG chặn được Directory.EnumerateFiles (xem báo cáo Task 4
    /// vòng fix 1 để biết chi tiết vì sao không đáng tin cậy trong CI). Thay vào đó test thẳng seam
    /// nội bộ <see cref="GcnTreeExporter.ContainsGcnFileCore"/> — ĐÚNG đường code mà ContainsGcnFile
    /// dùng thật, chỉ thay enumerate thật bằng một delegate ném lỗi để mô phỏng chính xác tình huống.
    /// </summary>
    private static void TreeExporterTreatsUnreadableSubFolderAsPossiblyContainingGcn()
    {
        var rules = TestRules();
        var warnings = new List<string>();

        var result = GcnTreeExporter.ContainsGcnFileCore(
            @"C:\gia-lap\khong-doc-duoc",
            () => throw new UnauthorizedAccessException("Giả lập mất quyền đọc thư mục con"),
            rules,
            ScannedSet(),
            warnings);

        AssertTrue(result, "Enumerate lỗi phải fail-CLOSED: trả true (coi như CÓ THỂ chứa GCN)");
        AssertEqual(1, warnings.Count, "Phải cộng đúng một cảnh báo khi enumerate lỗi");
        AssertTrue(warnings[0].Contains("khong-doc-duoc"), "Cảnh báo phải nêu rõ đường dẫn thư mục bị bỏ qua");

        // Đường "thành công": enumerate không lỗi thì trả kết quả Match bình thường, không cộng cảnh báo.
        var warningsOk = new List<string>();
        var noGcn = GcnTreeExporter.ContainsGcnFileCore(
            @"C:\gia-lap\co-doc-duoc", () => new[] { "don dang ky.pdf" }, rules, ScannedSet(), warningsOk);
        AssertFalse(noGcn, "Enumerate thành công, không có file GCN → false");
        AssertEqual(0, warningsOk.Count, "Đường thành công không cộng cảnh báo");

        var coGcn = GcnTreeExporter.ContainsGcnFileCore(
            @"C:\gia-lap\co-gcn", () => new[] { "GCN-01.pdf" }, rules, ScannedSet(), warningsOk);
        AssertTrue(coGcn, "Enumerate thành công, có file GCN → true");

        // Đổi GcnKeyword sau khi quét: tên file KHÔNG còn khớp rule, nhưng đường dẫn nằm trong danh
        // sách file đã quét ⇒ vẫn phải coi thư mục đó là CÓ chứa GCN (không copy lồng vào nhãn khác).
        var doiKeyword = new GcnFolderRules { GcnKeyword = "GIAY CHUNG NHAN", Groups = rules.Groups };
        var theoDuongDan = GcnTreeExporter.ContainsGcnFileCore(
            @"C:\gia-lap\doi-keyword",
            () => new[] { @"C:\gia-lap\doi-keyword\GCN-01.pdf" },
            doiKeyword,
            ScannedSet(@"C:\gia-lap\doi-keyword\GCN-01.pdf"),
            warningsOk);
        AssertTrue(theoDuongDan, "File đã quét vẫn được nhận là GCN dù GcnKeyword đã đổi");
    }
}
