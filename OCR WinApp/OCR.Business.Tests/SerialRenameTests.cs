using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Configuration;
using OCR.Business.Ocr;
using OCR.Business.SerialRename;
using OCR_WinApp.ViewModels;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

// Test cho màn "Đổi tên theo Serial": tách serial khỏi text OCR, toán vùng cắt 4 hướng,
// bộ dựng cây kết quả, và hai hàm thuần của ViewModel.
internal static partial class Program
{
    private static async Task RunSerialRenameTests()
    {
        await SerialReaderReadsSerialPrintedAtBottomRightOfRealPdf();
        await SerialReaderStillReadsWhenPageIsRotated();
        await SerialReaderReportsFailureInsteadOfGuessing();
        SerialRenameOptionsHaveSaneDefaults();
        SerialTextExtractsBothOldAndQrFormats();
        SerialTextRejectsLookalikeNumbers();
        SerialTextBuildsSafeFolderName();
        SerialCornerBoundsMapsEachRotationToBottomRightOfRotatedImage();
        SerialCornerBoundsSwapsRatiosForQuarterTurns();
        SerialTreeRenamesFolderAndGcnBySerial();
        SerialTreeKeepsOriginalNamesWhenSerialMissing();
        SerialTreeSplitsFolderWithTwoGcnIntoSiblingLabels();
        SerialTreeSuffixesDuplicateSerialInSameParent();
        SerialTreeMirrorsFoldersWithoutAnyGcn();
        SerialViewModelFiltersByGcnKeywordRecursively();
        SerialViewModelRejectsDestinationInsideSource();
    }

    // ------------------------------------------- luồng thật: render → cắt → OCR Windows → regex

    /// <summary>
    /// Tạo PDF có chuỗi <paramref name="serial"/> in ở GÓC DƯỚI PHẢI trang 1, giống vị trí số phát hành
    /// trên GCN. Chữ đen trên nền trắng nên đây là phép kiểm ĐƯỜNG ỐNG (render → cắt đúng góc → OCR →
    /// regex), KHÔNG phải phép đo độ chính xác trên bản scan thật (chữ đỏ, nền hoa văn).
    /// </summary>
    private static string CreateSerialPdf(string path, string serial, int rotate = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Rotate = rotate;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Nội dung giả ở nửa trên để chắc chắn phép cắt góc đang loại đúng phần không liên quan.
            gfx.DrawString("GIAY CHUNG NHAN QUYEN SU DUNG DAT",
                new XFont("Arial", 18), XBrushes.Black, new XPoint(60, 90));

            // Serial ở góc dưới phải, nằm gọn trong vùng 45% x 25% tính từ hai mép.
            gfx.DrawString(serial, new XFont("Arial", 28), XBrushes.Black,
                new XPoint(page.Width.Point - 210, page.Height.Point - 60));
        }

        doc.Save(path);
        return path;
    }

    private static SerialReader NewSerialReader() =>
        new(new LocalOcrEngine(), AppSettingsLoader.LoadSerialRename());

    private static async Task SerialReaderReadsSerialPrintedAtBottomRightOfRealPdf()
    {
        var root = NewSerialTempDir();
        try
        {
            var path = CreateSerialPdf(Path.Combine(root, "GCN-test.pdf"), "AA 123456");
            var scan = await NewSerialReader().ReadAsync(path, "GCN-test.pdf", CancellationToken.None);

            AssertEqual(SerialReadStatus.Read, scan.Status, "Đọc được serial in ở góc dưới phải trang 1");
            AssertEqual("AA 123456", scan.Serial, "Serial đọc ra đúng và đã chuẩn hoá");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    /// <summary>
    /// Trang bị quay 180° thì "góc dưới phải của tờ giấy" hiện ra ở góc TRÊN TRÁI của ảnh render.
    /// Đây là phép kiểm cho toàn bộ logic thử 4 hướng — nếu toán vùng cắt sai, test này fail.
    /// </summary>
    private static async Task SerialReaderStillReadsWhenPageIsRotated()
    {
        var root = NewSerialTempDir();
        try
        {
            foreach (var rotate in new[] { 90, 180, 270 })
            {
                var path = CreateSerialPdf(Path.Combine(root, $"GCN-quay-{rotate}.pdf"), "CK 06654954", rotate);
                var scan = await NewSerialReader().ReadAsync(path, $"GCN-quay-{rotate}.pdf", CancellationToken.None);

                AssertEqual(SerialReadStatus.Read, scan.Status,
                    $"Trang quay {rotate}° vẫn đọc được serial (note: {scan.Note})");
                AssertEqual("CK 06654954", scan.Serial, $"Trang quay {rotate}°: serial đọc ra đúng");
            }
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static async Task SerialReaderReportsFailureInsteadOfGuessing()
    {
        var root = NewSerialTempDir();
        try
        {
            var reader = NewSerialReader();

            // Trang không có gì khớp mẫu serial -> phải báo NotFound, KHÔNG được đoán bừa.
            var blank = Path.Combine(root, "GCN-trong.pdf");
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Save(blank);
            }
            var blankScan = await reader.ReadAsync(blank, "GCN-trong.pdf", CancellationToken.None);
            AssertEqual(SerialReadStatus.NotFound, blankScan.Status, "Trang trắng: không tìm thấy serial");
            AssertEqual("", blankScan.Serial, "Không đoán bừa serial khi không đọc được");

            // PDF hỏng -> OpenError kèm lý do để ghi log.
            var broken = Path.Combine(root, "GCN-hong.pdf");
            File.WriteAllText(broken, "%PDF-1.4 day khong phai pdf that");
            var brokenScan = await reader.ReadAsync(broken, "GCN-hong.pdf", CancellationToken.None);
            AssertEqual(SerialReadStatus.OpenError, brokenScan.Status, "PDF hỏng: OpenError");
            AssertTrue(brokenScan.Note.Length > 0, "PDF hỏng phải kèm lý do để ghi log");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    // -------------------------------------------------------------- cấu hình

    private static void SerialRenameOptionsHaveSaneDefaults()
    {
        var opt = AppSettingsLoader.LoadSerialRename();
        AssertTrue(opt.Workers > 0, "SerialRename.Workers phải > 0");
        AssertTrue(opt.RenderDpi >= 72, "SerialRename.RenderDpi phải hợp lý (>= 72)");
        AssertTrue(opt.CropRightRatio > 0 && opt.CropRightRatio <= 1, "CropRightRatio nằm trong (0,1]");
        AssertTrue(opt.CropBottomRatio > 0 && opt.CropBottomRatio <= 1, "CropBottomRatio nằm trong (0,1]");
        AssertTrue(opt.GcnKeyword.Length > 0, "GcnKeyword không được rỗng");
    }

    // -------------------------------------------------------------- tách serial

    private static void SerialTextExtractsBothOldAndQrFormats()
    {
        // Dạng liền — dạng in phổ biến nhất trên giấy, regex phục hồi từ tên file KHÔNG nhận dạng này.
        AssertEqual("AA 123456", GcnSerialText.TryExtract("AA123456"), "Mẫu cũ 6 số, viết liền");
        AssertEqual("CK 06654954", GcnSerialText.TryExtract("CK06654954"), "Mẫu QR 8 số, viết liền");
        AssertEqual("AA 123456", GcnSerialText.TryExtract("AA 123456"), "Có khoảng trắng");
        AssertEqual("BD 123456", GcnSerialText.TryExtract("So phat hanh: BD 123456"), "Có nhãn đứng trước");
        AssertEqual("AA 123456", GcnSerialText.TryExtract("aa123456"), "Chữ thường được in hoa lại");
        AssertEqual("D 123456", GcnSerialText.TryExtract("D 123456"), "Một chữ cái");
    }

    private static void SerialTextRejectsLookalikeNumbers()
    {
        // Mã vạch 13 số: KHÔNG được nhận vì (?!\d) chặn dãy số dài hơn.
        AssertEqual("", GcnSerialText.TryExtract("0739023057278"), "Mã vạch 13 số không phải serial");
        AssertEqual("", GcnSerialText.TryExtract("AB1234567"), "7 số không khớp mẫu 6 hoặc 8");
        AssertEqual("", GcnSerialText.TryExtract("ABC123456"), "3 chữ cái không khớp mẫu");
        AssertEqual("", GcnSerialText.TryExtract("123456"), "Chỉ có số, thiếu chữ cái");
        AssertEqual("", GcnSerialText.TryExtract(""), "Chuỗi rỗng");
        AssertEqual("", GcnSerialText.TryExtract(null), "null");
        // OCR đọc 0 thành O thì KHÔNG tự sửa: đặt sai tên thư mục tai hại hơn báo không đọc được.
        AssertEqual("", GcnSerialText.TryExtract("AA12O456"), "Không tự sửa nhầm lẫn O/0 của OCR");
    }

    private static void SerialTextBuildsSafeFolderName()
    {
        AssertEqual("AA 123456", GcnSerialText.ToFolderName("AA 123456"), "Giữ nguyên khoảng trắng đơn");
        AssertEqual("AA 123456", GcnSerialText.ToFolderName("  AA   123456  "), "Gộp khoảng trắng, trim");
        AssertEqual("", GcnSerialText.ToFolderName(""), "Rỗng vào, rỗng ra");
        AssertFalse(GcnSerialText.ToFolderName("AA/123456").Contains('/'),
            "Ký tự không hợp lệ trong tên file phải bị thay");
        AssertFalse(GcnSerialText.ToFolderName("AA 123456.").EndsWith('.'),
            "Windows cắt dấu chấm cuối tên thư mục nên phải trim trước");
    }

    // -------------------------------------------------------------- toán vùng cắt

    /// <summary>
    /// Vùng cắt phải luôn là góc dưới phải và phải NẰM GỌN trong kích thước ảnh ĐÃ QUAY. Ràng buộc thứ
    /// hai là ràng buộc thật của WinRT: vượt ra là <c>GetSoftwareBitmapAsync</c> ném E_INVALIDARG — đúng
    /// lỗi đã bắt được bằng test end-to-end với trang quay 90°.
    /// </summary>
    private static void SerialCornerBoundsMapsEachRotationToBottomRightOfRotatedImage()
    {
        const uint w = 800, h = 1200;
        foreach (var rotation in new[]
                 {
                     SerialRotation.None, SerialRotation.Clockwise90,
                     SerialRotation.Clockwise180, SerialRotation.Clockwise270
                 })
        {
            var b = SerialCornerBounds.Compute(w, h, rotation, 0.4, 0.2);

            bool swapped = rotation is SerialRotation.Clockwise90 or SerialRotation.Clockwise270;
            uint rotW = swapped ? h : w;
            uint rotH = swapped ? w : h;

            AssertEqual(rotW, b.X + b.Width, $"Quay {(int)rotation}: vùng cắt sát mép PHẢI ảnh đã quay");
            AssertEqual(rotH, b.Y + b.Height, $"Quay {(int)rotation}: vùng cắt sát mép DƯỚI ảnh đã quay");
            AssertTrue(b.Width > 0 && b.Height > 0, $"Quay {(int)rotation}: vùng cắt không được rỗng");
        }
    }

    private static void SerialCornerBoundsSwapsRatiosForQuarterTurns()
    {
        const uint w = 1000, h = 2000;

        var straight = SerialCornerBounds.Compute(w, h, SerialRotation.None, 0.4, 0.2);
        AssertEqual(400u, straight.Width, "Không quay: bề rộng = 40% của 1000");
        AssertEqual(400u, straight.Height, "Không quay: chiều cao = 20% của 2000");

        // Quay 90°: ảnh đã quay là 2000x1000 nên tỉ lệ đo trên kích thước ĐÃ ĐỔI CHỖ.
        var quarter = SerialCornerBounds.Compute(w, h, SerialRotation.Clockwise90, 0.4, 0.2);
        AssertEqual(800u, quarter.Width, "Quay 90°: bề rộng = 40% của 2000 (chiều cao gốc)");
        AssertEqual(200u, quarter.Height, "Quay 90°: chiều cao = 20% của 1000 (chiều rộng gốc)");
        AssertEqual(2000u, quarter.X + quarter.Width, "Quay 90°: vẫn phải nằm gọn trong ảnh đã quay");
        AssertEqual(1000u, quarter.Y + quarter.Height, "Quay 90°: vẫn phải nằm gọn trong ảnh đã quay");

        // Ảnh siêu nhỏ / tỉ lệ vô lý không được sinh vùng cắt rỗng (OCR sẽ ném lỗi).
        var tiny = SerialCornerBounds.Compute(3, 3, SerialRotation.None, 0.0001, 0.0001);
        AssertTrue(tiny.Width >= 1 && tiny.Height >= 1, "Vùng cắt luôn có ít nhất 1 điểm ảnh");
    }

    // -------------------------------------------------------------- dựng cây

    private static string NewSerialTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteSerialFile(string dir, string name, string content = "noi dung")
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static SerialScan ReadScan(string root, string path, string serial) => new()
    {
        SourcePath = path,
        RelativePath = Path.GetRelativePath(root, path),
        Status = SerialReadStatus.Read,
        Serial = serial
    };

    private static SerialScan FailedScan(string root, string path) => new()
    {
        SourcePath = path,
        RelativePath = Path.GetRelativePath(root, path),
        Status = SerialReadStatus.NotFound,
        Note = "khong tim thay serial"
    };

    private static void SerialTreeRenamesFolderAndGcnBySerial()
    {
        var root = NewSerialTempDir();
        try
        {
            var src = Path.Combine(root, "UongBi");
            var hoSo = Path.Combine(src, "Phuong1", "HoSo05");
            var gcn = WriteSerialFile(hoSo, "GCN-quyen-su-dung.pdf");
            WriteSerialFile(hoSo, "GT.pdf");
            WriteSerialFile(hoSo, "anh-mat-truoc.jpg");

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(dest);

            var result = new SerialRenameTreeExporter().Export(
                new SerialRenameExportRequest(src, dest, [ReadScan(src, gcn, "AA 123456")]));

            var labelDir = Path.Combine(dest, "UongBi", "Phuong1", "AA 123456");
            AssertTrue(Directory.Exists(labelDir),
                "Thư mục hồ sơ phải được đổi tên thành serial, đặt ĐÚNG vị trí cũ trong cây");
            AssertFalse(Directory.Exists(Path.Combine(dest, "UongBi", "Phuong1", "HoSo05")),
                "Tên thư mục gốc không còn xuất hiện khi đã đọc được serial");

            AssertTrue(File.Exists(Path.Combine(labelDir, "AA 123456-GCN.pdf")), "File GCN đổi tên theo serial");
            AssertTrue(File.Exists(Path.Combine(labelDir, "GT.pdf")), "File kèm theo giữ nguyên tên");
            AssertTrue(File.Exists(Path.Combine(labelDir, "anh-mat-truoc.jpg")), "Ảnh kèm theo giữ nguyên tên");
            AssertEqual(1, result.LabelFolders, "Một thư mục nhãn");
            AssertEqual(1, result.GcnRenamed, "Một file GCN đổi tên");

            // Thư mục nguồn KHÔNG được đụng tới.
            AssertTrue(File.Exists(gcn), "File nguồn phải còn nguyên tên cũ");
            AssertTrue(Directory.Exists(hoSo), "Thư mục nguồn phải còn nguyên tên cũ");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static void SerialTreeKeepsOriginalNamesWhenSerialMissing()
    {
        var root = NewSerialTempDir();
        try
        {
            var src = Path.Combine(root, "UongBi");
            var hoSo = Path.Combine(src, "HoSo01");
            var gcn = WriteSerialFile(hoSo, "GCN-khong-doc-duoc.pdf");
            WriteSerialFile(hoSo, "GT.pdf");

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(dest);

            var result = new SerialRenameTreeExporter().Export(
                new SerialRenameExportRequest(src, dest, [FailedScan(src, gcn)]));

            var kept = Path.Combine(dest, "UongBi", "HoSo01");
            AssertTrue(Directory.Exists(kept), "Không đọc được serial thì GIỮ NGUYÊN tên thư mục");
            AssertTrue(File.Exists(Path.Combine(kept, "GCN-khong-doc-duoc.pdf")),
                "File GCN không đọc được phải GIỮ NGUYÊN tên, không bị bỏ mất khỏi cây đích");
            AssertTrue(File.Exists(Path.Combine(kept, "GT.pdf")), "File kèm theo vẫn được copy");
            AssertEqual(0, result.LabelFolders, "Không tạo thư mục nhãn nào");
            AssertEqual(1, result.FoldersKeptOriginalName, "Một thư mục giữ nguyên tên");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static void SerialTreeSplitsFolderWithTwoGcnIntoSiblingLabels()
    {
        var root = NewSerialTempDir();
        try
        {
            var src = Path.Combine(root, "UongBi");
            var hoSo = Path.Combine(src, "HoSoGop");
            var gcnA = WriteSerialFile(hoSo, "GCN-a.pdf");
            var gcnB = WriteSerialFile(hoSo, "GCN-b.pdf");
            var gcnLoi = WriteSerialFile(hoSo, "GCN-loi.pdf");
            WriteSerialFile(hoSo, "GT.pdf");

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(dest);

            new SerialRenameTreeExporter().Export(new SerialRenameExportRequest(src, dest,
            [
                ReadScan(src, gcnA, "AA 111111"),
                ReadScan(src, gcnB, "BB 222222"),
                FailedScan(src, gcnLoi)
            ]));

            var dirA = Path.Combine(dest, "UongBi", "AA 111111");
            var dirB = Path.Combine(dest, "UongBi", "BB 222222");
            AssertTrue(Directory.Exists(dirA) && Directory.Exists(dirB),
                "Hai GCN đọc được serial phải thành hai thư mục nhãn NGANG HÀNG");

            AssertTrue(File.Exists(Path.Combine(dirA, "AA 111111-GCN.pdf")), "Nhãn A có đúng GCN của mình");
            AssertFalse(File.Exists(Path.Combine(dirA, "BB 222222-GCN.pdf")),
                "Nhãn A KHÔNG được chứa GCN của nhãn B");
            AssertTrue(File.Exists(Path.Combine(dirB, "BB 222222-GCN.pdf")), "Nhãn B có đúng GCN của mình");

            // File kèm theo + GCN không đọc được: nhân bản vào MỌI thư mục nhãn, giữ nguyên tên.
            foreach (var dir in new[] { dirA, dirB })
            {
                AssertTrue(File.Exists(Path.Combine(dir, "GT.pdf")),
                    $"File kèm theo phải có trong mọi thư mục nhãn ({Path.GetFileName(dir)})");
                AssertTrue(File.Exists(Path.Combine(dir, "GCN-loi.pdf")),
                    $"GCN không đọc được serial đi theo diện file kèm theo, giữ nguyên tên ({Path.GetFileName(dir)})");
            }
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static void SerialTreeSuffixesDuplicateSerialInSameParent()
    {
        var root = NewSerialTempDir();
        try
        {
            var src = Path.Combine(root, "UongBi");
            var gcnA = WriteSerialFile(Path.Combine(src, "HoSo01"), "GCN-a.pdf");
            var gcnB = WriteSerialFile(Path.Combine(src, "HoSo02"), "GCN-b.pdf");

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(dest);

            new SerialRenameTreeExporter().Export(new SerialRenameExportRequest(src, dest,
            [
                ReadScan(src, gcnA, "AA 999999"),
                ReadScan(src, gcnB, "AA 999999")
            ]));

            var first = Path.Combine(dest, "UongBi", "AA 999999");
            var second = Path.Combine(dest, "UongBi", "AA 999999_2");
            AssertTrue(Directory.Exists(first), "Thư mục nhãn đầu giữ tên serial trơn");
            AssertTrue(Directory.Exists(second),
                "Hai hồ sơ khác nhau trùng serial trong cùng thư mục cha đích thì thư mục sau nhận hậu tố _2");
            AssertTrue(File.Exists(Path.Combine(second, "AA 999999_2-GCN.pdf")),
                "Tên file GCN đi theo tên thư mục nhãn đã cấp");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static void SerialTreeMirrorsFoldersWithoutAnyGcn()
    {
        var root = NewSerialTempDir();
        try
        {
            var src = Path.Combine(root, "UongBi");
            // Nhánh hoàn toàn không có GCN — vẫn phải có mặt đủ ở cây đích.
            WriteSerialFile(Path.Combine(src, "TaiLieuChung", "Anh"), "so-do.jpg");
            WriteSerialFile(Path.Combine(src, "TaiLieuChung"), "huong-dan.docx");
            WriteSerialFile(src, "ghi-chu-goc.txt");
            var gcn = WriteSerialFile(Path.Combine(src, "HoSo01"), "GCN-a.pdf");

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(dest);

            new SerialRenameTreeExporter().Export(new SerialRenameExportRequest(src, dest,
                [ReadScan(src, gcn, "AA 123456")]));

            var outRoot = Path.Combine(dest, "UongBi");
            AssertTrue(File.Exists(Path.Combine(outRoot, "ghi-chu-goc.txt")),
                "File rời ở gốc nguồn phải được copy sang gốc cây đích");
            AssertTrue(File.Exists(Path.Combine(outRoot, "TaiLieuChung", "huong-dan.docx")),
                "Thư mục không có GCN phải được mirror nguyên tên");
            AssertTrue(File.Exists(Path.Combine(outRoot, "TaiLieuChung", "Anh", "so-do.jpg")),
                "Thư mục con nhiều cấp không có GCN vẫn phải được mirror");
            AssertTrue(Directory.Exists(Path.Combine(outRoot, "AA 123456")),
                "Nhánh có GCN vẫn được đổi tên theo serial");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    // -------------------------------------------------------------- hàm thuần của ViewModel

    private static void SerialViewModelFiltersByGcnKeywordRecursively()
    {
        var root = NewSerialTempDir();
        try
        {
            WriteSerialFile(root, "GCN-1.pdf");
            WriteSerialFile(Path.Combine(root, "con"), "scan gcn 2.PDF");
            WriteSerialFile(Path.Combine(root, "con", "sau"), "GCN-3.pdf");
            WriteSerialFile(root, "don-dang-ky.pdf");
            WriteSerialFile(root, "GCN-4.jpg");

            var files = DoiTenSerialViewModel.CollectGcnFiles(root, "GCN");

            AssertEqual(3, files.Count, "Quét đệ quy, chỉ nhận .pdf có cụm GCN trong tên");
            AssertTrue(files.Any(f => f.EndsWith("scan gcn 2.PDF", StringComparison.OrdinalIgnoreCase)),
                "gcn viết thường và đuôi .PDF hoa vẫn phải được nhận");
            AssertFalse(files.Any(f => f.EndsWith("don-dang-ky.pdf", StringComparison.OrdinalIgnoreCase)),
                "PDF không có cụm GCN bị loại");
            AssertFalse(files.Any(f => f.EndsWith("GCN-4.jpg", StringComparison.OrdinalIgnoreCase)),
                "Ảnh bị loại kể cả khi tên có GCN");
        }
        finally { DeleteIlisUbTempDir(root); }
    }

    private static void SerialViewModelRejectsDestinationInsideSource()
    {
        AssertTrue(DoiTenSerialViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo\KetQua"),
            "Thư mục đích nằm trong nguồn phải bị chặn");
        AssertTrue(DoiTenSerialViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo"),
            "Thư mục đích trùng nguồn phải bị chặn");
        AssertFalse(DoiTenSerialViewModel.IsDestinationInsideSource(@"E:\HoSo", @"E:\HoSo2"),
            "Thư mục cùng tiền tố nhưng khác hẳn thì KHÔNG được chặn nhầm");
        AssertFalse(DoiTenSerialViewModel.IsDestinationInsideSource(null, @"D:\KetQua"),
            "Chưa chọn nguồn thì không chặn");
    }
}
