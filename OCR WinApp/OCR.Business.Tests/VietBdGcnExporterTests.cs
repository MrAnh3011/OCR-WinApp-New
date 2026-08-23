using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;
using OCR.Business.Models;
using OCR.Business.VietBdGcn;

// Test cho pipeline VietBD: exporter khuôn Việt Bản Đồ (sheet "KeKhaiDangKy", dữ liệu từ dòng 5)
// và tính TÁCH RIÊNG khỏi màn iLIS (prompt/schema/cache/options đều riêng).
// Khác biệt cốt lõi khi ghi Excel: mỗi MĐSD của thửa là MỘT dòng riêng, và đồng sử dụng đẻ thêm
// dòng cho từng chủ (template chỉ có 2 khối chủ CHU_/VC_).
internal static partial class Program
{
    private static void RunVietBdGcnExporterTests()
    {
        VietBdExporterWritesOneRowPerSingleParcelSingleMdsd();
        VietBdExporterSplitsEachMdsdIntoItsOwnRow();
        VietBdExporterMultipliesParcelsByMdsd();
        VietBdExporterWritesSpouseBlockForVoChong();
        VietBdExporterExpandsCoOwnersIntoRows();
        VietBdExporterWritesFallbackRowWhenParcelRowsAreEmpty();
        VietBdExporterFlagsOnlyRealDuplicateParcels();
        VietBdExporterWritesLoaiGcnCodeStraightFromModel();
        VietBdExporterWritesUsageDeadlineOnlyForDateLikeTerm();
        VietBdExporterWritesUserEnteredMaXaIntoColumnB();
        VietBdExporterSeparatesNotesFromChangeHistory();
        VietBdExporterWritesHouseBlockOnEveryRowOfTheParcel();
        VietBdExporterLeavesHouseBlockEmptyWhenNoAsset();
        VietBdExporterThrowsWhenTemplateMissing();
        VietBdExporterWritesIntoRealTemplateKeepingHeaders();
        VietBdPipelineIsFullySeparatedFromIlisScreen();
        VietBdPromptDropsLocalLookupTablesAndUsesVietBdCodes();
    }

    private static string CreateVietBdTemplate(string root)
    {
        var templatePath = Path.Combine(root, "vietbd-template.xlsx");
        using var template = new XLWorkbook();
        template.AddWorksheet("KeKhaiDangKy");
        template.SaveAs(templatePath);
        return templatePath;
    }

    private static VietBdGcnEnvelope CreateVietBdEnvelope(
        string serial,
        string relationship,
        List<VietBdGcnOwner> owners,
        List<VietBdGcnRow> parcels)
        => new()
        {
            ten_file = serial + "-GCN.pdf",
            thong_tin_gcn = new VietBdGcnInfo
            {
                so_serial = serial,
                loai_quan_he = relationship,
                ma_vach = "0739023057278",
                // Prompt riêng của VietBD trả THẲNG mã danh mục, không trả tên rồi để phần mềm tra bảng.
                ma_loai_gcn = "11",
                ten_loai_gcn = "Giấy chứng nhận QSDĐƠ & QSHNƠ và TSKGLVĐ theo NĐ 43/NĐ-CP",
                chu_su_dung_chi_tiet = owners
            },
            danh_sach_dong = parcels
        };

    private static VietBdGcnRow CreateVietBdParcel(string sheet, string parcel, params VietBdGcnMdsd[] mdsd)
        => new()
        {
            td_so_to = sheet,
            td_so_thua = parcel,
            td_tong_dien_tich = "100",
            loai_thua_dat = "A",
            ten_don_vi_do = "Trung tâm kỹ thuật tài nguyên và môi trường",
            ngay_hoan_thanh_do = "14/08/2017",
            ky_so_vao_so = "CS00123",
            ky_ngay_ky_gcn = "20/01/2016",
            ky_nguoi_ky = "Nguyễn Hải Khiên",
            muc_dich_su_dung = mdsd.ToList()
        };

    private static void VietBdExporterWritesOneRowPerSingleParcelSingleMdsd()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "CK 123456",
                "ca_nhan",
                [
                    new VietBdGcnOwner
                    {
                        ho_ten = "Nguyễn Văn A",
                        nam_sinh = "1975",
                        gioi_tinh = "1",
                        loai_giay_to = "CCCD",
                        so_giay_to = "031075000851",
                        ngay_cap = "09/03/2015",
                        noi_cap = "Cục Cảnh sát",
                        so_nha_ngo = "Số 197",
                        duong_pho = "Trần Nguyên Hãn",
                        to_dan_pho = "TDP 5",
                        ma_xa = "11383",
                        // Prompt riêng tách sẵn ba cấp — exporter KHÔNG tự cắt chuỗi địa chỉ nữa.
                        xa = "phường Lê Chân",
                        huyen = "quận Lê Chân",
                        tinh = "thành phố Hải Phòng",
                        dia_chi_day_du = "Số 197 Trần Nguyên Hãn, phường Lê Chân, thành phố Hải Phòng"
                    }
                ],
                [CreateVietBdParcel("57", "7", new VietBdGcnMdsd { ma_mdsd = "ODT", dien_tich = "77.6", thoi_han_su_dung = "Lâu dài", ma_ngsd = "CNQ-KTT" })]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(1, rowCount, "VietBD: 1 thửa 1 MĐSD 1 chủ = 1 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("1", ws.Cell(5, "A").Value.ToString(), "STT cột A");
            AssertEqual("57-7", ws.Cell(5, "C").Value.ToString(), "Mã đơn = {số tờ}-{số thửa}");
            AssertEqual("0", ws.Cell(5, "M").Value.ToString(), "Không đồng sử dụng thì DDK_dongSuDung = 0");
            AssertEqual("CK123456", ws.Cell(5, "N").Value.ToString(), "Số phát hành bỏ khoảng trắng");
            AssertEqual("CS00123", ws.Cell(5, "O").Value.ToString(), "Số vào sổ");
            AssertEqual("057278", ws.Cell(5, "Q").Value.ToString(), "Số hồ sơ gốc = 6 số cuối mã vạch");
            AssertEqual("20/01/2016", ws.Cell(5, "R").Value.ToString(), "Ngày cấp");
            AssertEqual("Nguyễn Hải Khiên", ws.Cell(5, "S").Value.ToString(), "Người ký");
            AssertEqual("0739023057278", ws.Cell(5, "T").Value.ToString(), "Mã vạch");
            AssertEqual("11", ws.Cell(5, "W").Value.ToString(), "Loại GCN lấy thẳng mã do prompt trả về");
            AssertEqual("Cá nhân", ws.Cell(5, "Z").Value.ToString(), "Loại đối tượng");
            AssertEqual("CNV", ws.Cell(5, "AA").Value.ToString(), "Loại đối tượng sử dụng đất");
            AssertEqual("Nguyễn Văn A", ws.Cell(5, "AB").Value.ToString(), "Họ tên chủ");
            AssertEqual("1975", ws.Cell(5, "AC").Value.ToString(), "Năm sinh chủ");
            AssertEqual("1", ws.Cell(5, "AE").Value.ToString(), "Giới tính giữ nguyên 0/1 của model");
            AssertEqual("", ws.Cell(5, "AG").Value.ToString(), "Dân tộc KHÔNG được điền sẵn — GCN không in dân tộc, mặc định Kinh là suy đoán sai với người dân tộc thiểu số");
            AssertEqual("VNM", ws.Cell(5, "AH").Value.ToString(), "Quốc tịch = mã VNM");
            AssertEqual("11383", ws.Cell(5, "AN").Value.ToString(), "Mã xã của chủ");
            AssertEqual("phường Lê Chân", ws.Cell(5, "AO").Value.ToString(), "Tên xã lấy thẳng từ trường xa");
            AssertEqual("quận Lê Chân", ws.Cell(5, "AP").Value.ToString(), "Tên huyện lấy thẳng từ trường huyen");
            AssertEqual("thành phố Hải Phòng", ws.Cell(5, "AQ").Value.ToString(), "Tên tỉnh lấy thẳng từ trường tinh");
            AssertEqual("CCCD", ws.Cell(5, "AR").Value.ToString(), "Loại giấy tờ");
            AssertEqual("", ws.Cell(5, "B").Value.ToString(), "Không truyền mã xã thì cột B để trống");
            AssertEqual("", ws.Cell(5, "AV").Value.ToString(), "Không phải vợ chồng thì khối VC_ trống");
            AssertEqual("1", ws.Cell(5, "CB").Value.ToString(), "Loại bản đồ = mã 1 (VN2000)");
            AssertEqual("Toàn đạc điện tử", ws.Cell(5, "CD").Value.ToString(), "Phương pháp đo hằng số");
            AssertEqual("Cao", ws.Cell(5, "CE").Value.ToString(), "Mức độ chính xác hằng số");
            AssertEqual("14/08/2017", ws.Cell(5, "CG").Value.ToString(), "Ngày hoàn thành đo");
            AssertEqual("A", ws.Cell(5, "CH").Value.ToString(), "Loại thửa đất vào cột CH");
            AssertEqual("7", ws.Cell(5, "CI").Value.ToString(), "Số thứ tự thửa");
            AssertEqual("57", ws.Cell(5, "CJ").Value.ToString(), "Số hiệu tờ bản đồ");
            AssertEqual("", ws.Cell(5, "CM").Value.ToString(), "Diện tích bản đồ để trống");
            AssertEqual("100", ws.Cell(5, "CN").Value.ToString(), "Diện tích pháp lý");
            AssertEqual("ODT", ws.Cell(5, "CU").Value.ToString(), "Mã mục đích sử dụng");
            AssertEqual("77.6", ws.Cell(5, "CX").Value.ToString(), "Diện tích MĐSD");
            AssertEqual("Lâu dài", ws.Cell(5, "CZ").Value.ToString(), "Thời hạn sử dụng");
            AssertEqual("CNQ-KTT", ws.Cell(5, "DA").Value.ToString(), "Nguồn gốc sử dụng đất");
            AssertEqual("77.6", ws.Cell(5, "DC").Value.ToString(), "Diện tích nguồn gốc");
            AssertEqual("CK 123456-GCN.pdf", ws.Cell(5, "FE").Value.ToString(), "Đường dẫn hồ sơ quét = tên file nguồn");

            AssertEqual("EXTRA_doTinCay", ws.Cell(1, "FF").Value.ToString(), "Cột phụ có mã trường ở dòng 1");
            AssertEqual("Cảnh báo", ws.Cell(3, "FG").Value.ToString(), "Cột phụ có tên tiếng Việt ở dòng 3");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Khác biệt CỐT LÕI với khuôn iLIS: iLIS nhồi 4 MĐSD vào 4 khối cột cùng dòng, VietBD tách mỗi MĐSD một dòng.</summary>
    private static void VietBdExporterSplitsEachMdsdIntoItsOwnRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "CK 222222",
                "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Nguyễn Văn A" }],
                [
                    CreateVietBdParcel("57", "7",
                        new VietBdGcnMdsd { ma_mdsd = "ODT", dien_tich = "77.6", thoi_han_su_dung = "Lâu dài" },
                        new VietBdGcnMdsd { ma_mdsd = "BHK", dien_tich = "500", thoi_han_su_dung = "13/04/2076" })
                ]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(2, rowCount, "1 thửa 2 MĐSD = 2 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            // Cụm MĐSD đổi giữa hai dòng...
            AssertEqual("ODT", ws.Cell(5, "CU").Value.ToString(), "MĐSD dòng 1");
            AssertEqual("BHK", ws.Cell(6, "CU").Value.ToString(), "MĐSD dòng 2");
            AssertEqual("77.6", ws.Cell(5, "CX").Value.ToString(), "Diện tích MĐSD dòng 1");
            AssertEqual("500", ws.Cell(6, "CX").Value.ToString(), "Diện tích MĐSD dòng 2");

            // ...nhưng mọi thông tin GCN/chủ/thửa phải lặp lại nguyên vẹn.
            foreach (var col in new[] { "C", "N", "T", "W", "AB", "CH", "CI", "CJ", "CN" })
                AssertEqual(ws.Cell(5, col).Value.ToString(), ws.Cell(6, col).Value.ToString(),
                    $"Cột {col} phải lặp lại y hệt trên mọi dòng MĐSD của cùng một thửa");

            AssertEqual("2", ws.Cell(6, "A").Value.ToString(), "STT tăng dần theo từng dòng, không gom theo serial");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterMultipliesParcelsByMdsd()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "CK 333333",
                "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Nguyễn Văn A" }],
                [
                    CreateVietBdParcel("57", "7",
                        new VietBdGcnMdsd { ma_mdsd = "ODT" },
                        new VietBdGcnMdsd { ma_mdsd = "BHK" }),
                    CreateVietBdParcel("57", "12",
                        new VietBdGcnMdsd { ma_mdsd = "ODT" },
                        new VietBdGcnMdsd { ma_mdsd = "CLN" })
                ]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(4, rowCount, "2 thửa × 2 MĐSD = 4 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("57-7", ws.Cell(5, "C").Value.ToString(), "Mã đơn thửa 1");
            AssertEqual("57-7", ws.Cell(6, "C").Value.ToString(), "Mã đơn thửa 1 (MĐSD thứ hai)");
            AssertEqual("57-12", ws.Cell(7, "C").Value.ToString(), "Mã đơn thửa 2");
            AssertEqual("57-12", ws.Cell(8, "C").Value.ToString(), "Mã đơn thửa 2 (MĐSD thứ hai)");
            AssertEqual("CLN", ws.Cell(8, "CU").Value.ToString(), "MĐSD cuối cùng");

            for (int row = 5; row <= 8; row++)
                AssertFalse(ws.Row(row).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                    $"Dòng {row}: nhiều thửa/nhiều MĐSD của cùng một GCN KHÔNG phải trùng");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterWritesSpouseBlockForVoChong()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "CK 444444",
                "vo_chong",
                [
                    new VietBdGcnOwner { ho_ten = "Trần Văn Yến", nam_sinh = "1961", gioi_tinh = "1", loai_giay_to = "CCCD", so_giay_to = "031061002490" },
                    new VietBdGcnOwner { ho_ten = "Đặng Thị Sơ", nam_sinh = "1966", gioi_tinh = "0", loai_giay_to = "CCCD", so_giay_to = "031166002800", xa = "phường An Biên", tinh = "thành phố Hải Phòng" }
                ],
                [CreateVietBdParcel("57", "7", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(1, rowCount, "Vợ chồng vẫn chỉ 1 dòng cho mỗi MĐSD");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("Vợ chồng", ws.Cell(5, "Z").Value.ToString(), "Loại đối tượng = Vợ chồng");
            AssertEqual("0", ws.Cell(5, "M").Value.ToString(), "Vợ chồng KHÔNG phải đồng sử dụng");
            AssertEqual("Trần Văn Yến", ws.Cell(5, "AB").Value.ToString(), "Chủ ở khối CHU_");
            AssertEqual("Đặng Thị Sơ", ws.Cell(5, "AV").Value.ToString(), "Vợ/chồng ở khối VC_");
            AssertEqual("0", ws.Cell(5, "AY").Value.ToString(), "Giới tính vợ/chồng");
            AssertEqual("", ws.Cell(5, "BA").Value.ToString(), "Dân tộc vợ/chồng cũng không được điền sẵn");
            AssertEqual("VNM", ws.Cell(5, "BB").Value.ToString(), "Quốc tịch vợ/chồng");
            AssertEqual("phường An Biên", ws.Cell(5, "BI").Value.ToString(), "Xã của vợ/chồng");
            AssertEqual("", ws.Cell(5, "BJ").Value.ToString(), "Huyện của vợ/chồng để trống khi model trả null");
            AssertEqual("thành phố Hải Phòng", ws.Cell(5, "BK").Value.ToString(), "Tỉnh của vợ/chồng");
            AssertEqual("031166002800", ws.Cell(5, "BM").Value.ToString(), "Số giấy tờ vợ/chồng");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Template chỉ có 2 khối chủ (CHU_/VC_) nên đồng sử dụng phải đẻ thêm dòng, mỗi chủ đứng ở khối CHU_.</summary>
    private static void VietBdExporterExpandsCoOwnersIntoRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "CK 555555",
                "dong_su_dung",
                [
                    new VietBdGcnOwner { ho_ten = "Chủ Một" },
                    new VietBdGcnOwner { ho_ten = "Chủ Hai" },
                    new VietBdGcnOwner { ho_ten = "Chủ Ba" }
                ],
                [CreateVietBdParcel("57", "7", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(3, rowCount, "Đồng sử dụng 3 chủ = 3 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("Chủ Một", ws.Cell(5, "AB").Value.ToString(), "Chủ 1 ở khối CHU_");
            AssertEqual("Chủ Hai", ws.Cell(6, "AB").Value.ToString(), "Chủ 2 ở khối CHU_");
            AssertEqual("Chủ Ba", ws.Cell(7, "AB").Value.ToString(), "Chủ 3 ở khối CHU_");
            for (int row = 5; row <= 7; row++)
            {
                AssertEqual("1", ws.Cell(row, "M").Value.ToString(), $"Dòng {row}: DDK_dongSuDung = 1");
                AssertEqual("", ws.Cell(row, "AV").Value.ToString(), $"Dòng {row}: khối VC_ phải trống khi đồng sử dụng");
                AssertFalse(ws.Row(row).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                    $"Dòng {row}: các chủ đồng sử dụng của cùng một thửa không phải trùng");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterWritesFallbackRowWhenParcelRowsAreEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope(
                "DH 444331",
                "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Lê Thị Quyên" }],
                []);
            envelope.thong_tin_gcn.do_tin_cay = "trung_binh";
            envelope.thong_tin_gcn.canh_bao = ["Không tìm thấy thông tin thửa đất trong tài liệu cung cấp"];

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(1, rowCount, "GCN không có dòng thửa vẫn ghi 1 dòng fallback");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("DH444331", ws.Cell(5, "N").Value.ToString(), "Fallback vẫn có số phát hành");
            AssertEqual("Lê Thị Quyên", ws.Cell(5, "AB").Value.ToString(), "Fallback vẫn có chủ sử dụng");
            AssertEqual("", ws.Cell(5, "C").Value.ToString(), "Fallback không có tờ/thửa thì mã đơn để trống");
            AssertEqual("", ws.Cell(5, "CI").Value.ToString(), "Fallback: số thửa trống");
            AssertEqual("", ws.Cell(5, "CU").Value.ToString(), "Fallback: MĐSD trống");
            AssertEqual("trung_binh", ws.Cell(5, "FF").Value.ToString(), "Độ tin cậy vào cột phụ FF");
            AssertTrue(ws.Cell(5, "FG").Value.ToString().Contains("Không tìm thấy thông tin thửa đất", StringComparison.Ordinal),
                "Cảnh báo của model vào cột phụ FG");
            AssertTrue(ws.Row(5).Style.Fill.BackgroundColor.Equals(XLColor.Yellow),
                "Độ tin cậy trung bình phải tô vàng");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterFlagsOnlyRealDuplicateParcels()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var mdsd = new VietBdGcnMdsd { ma_mdsd = "ODT" };
            var first = CreateVietBdEnvelope("DUP 001", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "A" }], [CreateVietBdParcel("1", "01", mdsd)]);
            // Cùng serial nhưng KHÁC số thửa -> thửa khác của cùng GCN, không phải trùng.
            var otherParcel = CreateVietBdEnvelope("DUP 001", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "B" }], [CreateVietBdParcel("1", "99", mdsd)]);
            // Khớp cả serial + tờ + thửa -> đúng là trùng.
            var realDuplicate = CreateVietBdEnvelope("DUP 001", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "C" }], [CreateVietBdParcel("1", "01", mdsd)]);
            // Cùng tờ + thửa nhưng KHÁC serial -> không trùng.
            var otherSerial = CreateVietBdEnvelope("DUP 002", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "D" }], [CreateVietBdParcel("1", "01", mdsd)]);

            int rowCount = new VietBdGcnExcelExporter()
                .Write([first, otherParcel, realDuplicate, otherSerial], outputPath, templatePath);
            AssertEqual(4, rowCount, "Bốn GCN một thửa một MĐSD = 4 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            AssertFalse(ws.Row(6).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "Cùng serial khác thửa KHÔNG được coi là trùng");
            AssertEqual("", ws.Cell(6, "FG").Value.ToString(), "Cùng serial khác thửa không có cảnh báo");

            var warning = ws.Cell(7, "FG").Value.ToString();
            AssertTrue(warning.Contains("serial DUP 001", StringComparison.Ordinal), "Cảnh báo trùng nêu số serial");
            AssertTrue(warning.Contains("thửa 01", StringComparison.Ordinal), "Cảnh báo trùng nêu thửa");
            AssertTrue(warning.Contains("dòng 5", StringComparison.Ordinal), "Cảnh báo trùng nêu dòng đã ghi");
            AssertFalse(warning.Contains("dòng 5, 6", StringComparison.Ordinal), "Cảnh báo không được trỏ vào dòng thửa khác");
            AssertTrue(ws.Row(7).Style.Fill.BackgroundColor.Equals(XLColor.Red), "Dòng trùng thật phải tô đỏ");

            AssertFalse(ws.Row(8).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "Cùng thửa khác serial không phải trùng");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Prompt riêng của VietBD trả THẲNG mã danh mục nên exporter chỉ ghi lại nguyên giá trị —
    /// không còn bảng map tên → mã trong code (bảng mã của VietBD đánh số khác iLIS).
    /// </summary>
    private static void VietBdExporterWritesLoaiGcnCodeStraightFromModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var qr = CreateVietBdEnvelope("AA 06654954", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "A" }], [CreateVietBdParcel("1", "1", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);
            qr.thong_tin_gcn.ma_loai_gcn = "98";

            var unknown = CreateVietBdEnvelope("CK 666666", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "B" }], [CreateVietBdParcel("2", "2", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);
            unknown.thong_tin_gcn.ma_loai_gcn = null;

            new VietBdGcnExcelExporter().Write([qr, unknown], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("98", ws.Cell(5, "W").Value.ToString(), "Mã loại GCN mẫu QR ghi thẳng");
            AssertEqual("", ws.Cell(6, "W").Value.ToString(), "Model không xác định được loại GCN thì cột W để trống");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterWritesUsageDeadlineOnlyForDateLikeTerm()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope("CK 777777", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "A" }],
                [
                    CreateVietBdParcel("1", "1",
                        new VietBdGcnMdsd { ma_mdsd = "ODT", thoi_han_su_dung = "Lâu dài" },
                        new VietBdGcnMdsd { ma_mdsd = "CLN", thoi_han_su_dung = "13/04/2046" })
                ]);

            new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            AssertEqual("Lâu dài", ws.Cell(5, "CZ").Value.ToString(), "Thời hạn 'Lâu dài' ghi nguyên văn");
            AssertEqual("", ws.Cell(5, "CY").Value.ToString(), "'Lâu dài' không phải ngày -> không ghi ngày hết hạn");
            AssertEqual("13/04/2046", ws.Cell(6, "CZ").Value.ToString(), "Thời hạn dạng ngày ghi nguyên văn");
            AssertEqual("13/04/2046", ws.Cell(6, "CY").Value.ToString(), "Thời hạn dạng ngày cũng vào cột ngày hết hạn");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Cột B (DDK_maXa) không thể lấy từ OCR (giấy không in mã xã của thửa đất) nên người dùng nhập
    /// một lần ở hộp thoại lúc bấm Bắt đầu; giá trị đó phải xuất hiện ở MỌI dòng của cả lô.
    /// </summary>
    private static void VietBdExporterWritesUserEnteredMaXaIntoColumnB()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            // Lô 2 GCN: một GCN 1 thửa 2 MĐSD, một GCN đồng sử dụng 2 chủ -> tổng 4 dòng.
            var multiMdsd = CreateVietBdEnvelope("CK 111111", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "A" }],
                [CreateVietBdParcel("1", "1",
                    new VietBdGcnMdsd { ma_mdsd = "ODT" },
                    new VietBdGcnMdsd { ma_mdsd = "CLN" })]);
            var coOwned = CreateVietBdEnvelope("CK 222222", "dong_su_dung",
                [new VietBdGcnOwner { ho_ten = "B" }, new VietBdGcnOwner { ho_ten = "C" }],
                [CreateVietBdParcel("2", "2", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);

            // Chủ thứ nhất có mã xã đọc được từ giấy -> giá trị người dùng nhập vẫn phải thắng ở cột B,
            // còn cột AN (mã xã của CHỦ) vẫn giữ theo OCR vì đó là trường khác.
            multiMdsd.thong_tin_gcn.chu_su_dung_chi_tiet![0].ma_xa = "99999";

            int rowCount = new VietBdGcnExcelExporter().Write([multiMdsd, coOwned], outputPath, templatePath, " 11407 ");
            AssertEqual(4, rowCount, "Lô test phải ra 4 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            for (int row = 5; row <= 8; row++)
                AssertEqual("11407", ws.Cell(row, "B").Value.ToString(),
                    $"Dòng {row}: cột B phải là mã xã người dùng nhập (đã trim khoảng trắng)");

            AssertEqual("99999", ws.Cell(5, "AN").Value.ToString(),
                "Cột AN là mã xã của CHỦ, vẫn lấy theo OCR — không bị mã xã của đơn ghi đè");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ba mục khác nhau trên giấy phải vào ba cột khác nhau: ghi chú mặt 1 → `X`, mục "Ghi chú" ở mặt 2
    /// → `Y`, mục "Những thay đổi sau khi cấp GCN" → cột phụ `FH`.
    /// Trước đây "Những thay đổi" bị ghi nhầm vào cột `X` (GCN_ghiChuTrang1).
    /// </summary>
    private static void VietBdExporterSeparatesNotesFromChangeHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope("CK 121212", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Nguyễn Văn A" }],
                [CreateVietBdParcel("62", "79", new VietBdGcnMdsd { ma_mdsd = "ONT" })]);
            envelope.thong_tin_gcn.ghi_chu_trang_1 = ["Ghi chú nằm trên mặt 1"];
            envelope.thong_tin_gcn.ghi_chu = ["Số hiệu và diện tích thửa đất được xác định theo bản đồ địa chính"];
            envelope.thong_tin_gcn.thong_tin_thay_doi =
            [
                "Chuyển mục đích sử dụng từ BHK thành ONT theo Quyết định số 3471/QĐ-UBND ngày 10/9/2025",
                "Thời hạn sử dụng: Lâu dài theo hồ sơ số 111.CM.001.2025"
            ];

            new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            AssertEqual("1) Ghi chú nằm trên mặt 1", ws.Cell(5, "X").Value.ToString(),
                "Cột X (GCN_ghiChuTrang1) chỉ nhận ghi chú của mặt 1");
            AssertTrue(ws.Cell(5, "Y").Value.ToString().Contains("bản đồ địa chính", StringComparison.Ordinal),
                "Cột Y (GCN_ghiChuTrang2) nhận mục Ghi chú của mặt 2");

            var changes = ws.Cell(5, "FH").Value.ToString();
            AssertTrue(changes.Contains("Chuyển mục đích sử dụng", StringComparison.Ordinal),
                "Cột phụ FH nhận nội dung 'Những thay đổi sau khi cấp GCN'");
            AssertTrue(changes.Contains("111.CM.001.2025", StringComparison.Ordinal),
                "Cột FH phải gom đủ mọi dòng thay đổi (mặt 3 + mặt 4)");

            AssertFalse(ws.Cell(5, "X").Value.ToString().Contains("Chuyển mục đích", StringComparison.Ordinal),
                "Nội dung 'Những thay đổi' KHÔNG được lọt vào cột Ghi chú trang 1");
            AssertFalse(ws.Cell(5, "Y").Value.ToString().Contains("Chuyển mục đích", StringComparison.Ordinal),
                "Nội dung 'Những thay đổi' KHÔNG được lọt vào cột Ghi chú trang 2");

            AssertEqual("EXTRA_thongTinThayDoi", ws.Cell(1, "FH").Value.ToString(), "Cột phụ FH có mã trường ở dòng 1");
            AssertEqual("Những thay đổi sau khi cấp GCN", ws.Cell(3, "FH").Value.ToString(), "Cột phụ FH có tên tiếng Việt ở dòng 3");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Thửa có "Nhà ở riêng lẻ" ở mục 3 → điền cụm DE…DX. Nhà thuộc về THỬA nên phải lặp lại trên
    /// MỌI dòng của thửa đó (khuôn VietBD nở dòng theo MĐSD × chủ), giống sheet KeKhaiDangKy_Mau.
    /// </summary>
    private static void VietBdExporterWritesHouseBlockOnEveryRowOfTheParcel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            // Thửa 1: có nhà, 2 MĐSD -> 2 dòng, cả hai phải mang y hệt dữ liệu nhà.
            var withHouse = CreateVietBdParcel("57", "179",
                new VietBdGcnMdsd { ma_mdsd = "ONT" },
                new VietBdGcnMdsd { ma_mdsd = "CLN" });
            withHouse.loai_thua_dat = "B";
            withHouse.nha_o = new VietBdGcnNhaO
            {
                ma_loai_nha_rieng_le = "1",
                ma_quyen_so_huu = "1",
                hinh_thuc_so_huu = "Sở hữu chung",
                ten_tai_san = "Nhà ở riêng lẻ - Nhà 1 tầng, sàn mái bằng bê tông cốt thép",
                dia_chi_day_du = "Thôn Trường Xuân, xã Đồng Tiến, huyện Cô Tô, tỉnh Quảng Ninh",
                to_dan_pho = "Thôn Trường Xuân",
                dien_tich_su_dung = "117,0",
                ket_cau = "Bê tông cốt thép",
                so_tang = "1"
            };

            // Thửa 2: KHÔNG có nhà -> cụm DE…DX phải trống, không dính dữ liệu của thửa 1.
            var noHouse = CreateVietBdParcel("57", "180", new VietBdGcnMdsd { ma_mdsd = "ONT" });

            var envelope = CreateVietBdEnvelope("AA 00101804", "vo_chong",
                [
                    new VietBdGcnOwner { ho_ten = "Bùi Văn Yên" },
                    new VietBdGcnOwner { ho_ten = "Phan Thị Hà" }
                ],
                [withHouse, noHouse]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(3, rowCount, "Thửa 1 (2 MĐSD) + thửa 2 (1 MĐSD) = 3 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            foreach (var row in new[] { 5, 6 })
            {
                AssertEqual("1", ws.Cell(row, "DE").Value.ToString(), $"Dòng {row}: mã loại nhà riêng lẻ");
                AssertEqual("1", ws.Cell(row, "DG").Value.ToString(), $"Dòng {row}: sở hữu chung = 1");
                AssertEqual("Nhà ở riêng lẻ - Nhà 1 tầng, sàn mái bằng bê tông cốt thép",
                    ws.Cell(row, "DH").Value.ToString(), $"Dòng {row}: tên tài sản nguyên văn");
                AssertEqual("Thôn Trường Xuân, xã Đồng Tiến, huyện Cô Tô, tỉnh Quảng Ninh",
                    ws.Cell(row, "DI").Value.ToString(), $"Dòng {row}: địa chỉ nhà");
                AssertEqual("Thôn Trường Xuân", ws.Cell(row, "DL").Value.ToString(), $"Dòng {row}: tổ dân phố của nhà");
                AssertEqual("117,0", ws.Cell(row, "DO").Value.ToString(), $"Dòng {row}: diện tích sử dụng");
                AssertEqual("Bê tông cốt thép", ws.Cell(row, "DR").Value.ToString(), $"Dòng {row}: kết cấu tách từ tên tài sản");
                AssertEqual("1", ws.Cell(row, "DS").Value.ToString(), $"Dòng {row}: số tầng tách từ tên tài sản");
                AssertEqual("B", ws.Cell(row, "CH").Value.ToString(), $"Dòng {row}: thửa có tài sản gắn liền = loại B");

                // Cột giấy không in phải để trống, không được bịa.
                foreach (var col in new[] { "DF", "DJ", "DK", "DM", "DN", "DP", "DQ", "DT", "DU", "DV", "DW", "DX" })
                    AssertEqual("", ws.Cell(row, col).Value.ToString(),
                        $"Dòng {row}: cột {col} giấy không in nên phải để trống");
            }

            // Dòng của thửa KHÔNG có nhà.
            foreach (var col in new[] { "DE", "DG", "DH", "DI", "DL", "DO", "DR", "DS" })
                AssertEqual("", ws.Cell(7, col).Value.ToString(),
                    $"Thửa không có nhà: cột {col} phải trống, không lây dữ liệu của thửa trước");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Mục 3 ghi `-/-` (không có tài sản) → toàn bộ cụm DE…DX để trống.</summary>
    private static void VietBdExporterLeavesHouseBlockEmptyWhenNoAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = CreateVietBdTemplate(root);
            var outputPath = Path.Combine(root, "output.xlsx");

            var envelope = CreateVietBdEnvelope("CK 131313", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Nguyễn Văn A" }],
                [CreateVietBdParcel("1", "1", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);

            new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");
            foreach (var col in new[] { "DE", "DF", "DG", "DH", "DI", "DJ", "DK", "DL", "DM", "DN", "DO", "DP", "DQ", "DR", "DS", "DT", "DU", "DV", "DW", "DX" })
                AssertEqual("", ws.Cell(5, col).Value.ToString(), $"Không có tài sản: cột {col} phải trống");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VietBdExporterThrowsWhenTemplateMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var missing = Path.Combine(root, "khong-ton-tai-vietbd-template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            var envelope = CreateVietBdEnvelope("CK 888888", "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "A" }], [CreateVietBdParcel("1", "1", new VietBdGcnMdsd { ma_mdsd = "ODT" })]);

            try
            {
                new VietBdGcnExcelExporter().Write([envelope], outputPath, missing);
                AssertTrue(false, "Thiếu template phải ném FileNotFoundException.");
            }
            catch (FileNotFoundException ex)
            {
                AssertTrue(ex.Message.Contains("khong-ton-tai-vietbd-template.xlsx", StringComparison.Ordinal),
                    "Thông điệp lỗi phải nêu tên template thiếu.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Chạy trên ĐÚNG file template thật trong Assets (không phải workbook giả): template có merged cell,
    /// data validation và nhiều sheet danh mục — đây là nơi ClosedXML dễ vỡ nhất (VD AutoFilter).
    /// </summary>
    private static void VietBdExporterWritesIntoRealTemplateKeepingHeaders()
    {
        var templatePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "Assets", "Temp", "Excel_Template_VietBD.xlsx"));
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var outputPath = Path.Combine(root, "output.xlsx");
            var envelope = CreateVietBdEnvelope(
                "CK 999999",
                "ca_nhan",
                [new VietBdGcnOwner { ho_ten = "Nguyễn Văn A", ma_xa = "11383", xa = "phường An Biên", tinh = "thành phố Hải Phòng" }],
                [
                    CreateVietBdParcel("57", "7",
                        new VietBdGcnMdsd { ma_mdsd = "ODT", dien_tich = "77.6", thoi_han_su_dung = "Lâu dài" },
                        new VietBdGcnMdsd { ma_mdsd = "BHK", dien_tich = "500", thoi_han_su_dung = "13/04/2076" })
                ]);

            int rowCount = new VietBdGcnExcelExporter().Write([envelope], outputPath, templatePath);
            AssertEqual(2, rowCount, "Template thật: 1 thửa 2 MĐSD = 2 dòng");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            // Header gốc của template phải còn nguyên (chỉ được xoá từ dòng 5 trở xuống).
            AssertEqual("STT", ws.Cell(1, "A").Value.ToString(), "Dòng 1 giữ mã trường gốc");
            AssertEqual("GCN_maVach", ws.Cell(1, "T").Value.ToString(), "Dòng 1 giữ mã trường GCN_maVach");
            AssertEqual("Số thứ tự thửa", ws.Cell(3, "CI").Value.ToString(), "Dòng 3 giữ tên tiếng Việt");
            // Dòng 4 là hàng đánh số cột của template; sheet KeKhaiDangKy đánh số KHÔNG liên tục (FE = 108,
            // khác sheet mẫu KeKhaiDangKy_Mau đánh 1..161) — chỉ cần khẳng định nó không bị exporter đụng vào.
            AssertEqual("108", ws.Cell(4, "FE").Value.ToString(), "Dòng 4 giữ nguyên số cột gốc của template");

            AssertEqual("57-7", ws.Cell(5, "C").Value.ToString(), "Template thật: mã đơn");
            AssertEqual("ODT", ws.Cell(5, "CU").Value.ToString(), "Template thật: MĐSD dòng 1");
            AssertEqual("BHK", ws.Cell(6, "CU").Value.ToString(), "Template thật: MĐSD dòng 2");
            AssertEqual("13/04/2076", ws.Cell(6, "CY").Value.ToString(), "Template thật: ngày hết hạn sử dụng");

            // Các sheet phụ của khuôn Việt Bản Đồ phải còn nguyên để file nộp được.
            AssertTrue(output.Worksheets.Any(s => s.Name == "ThongTinGiaoDich"), "Sheet ThongTinGiaoDich phải còn");
            AssertTrue(output.Worksheets.Any(s => s.Name == "DM_LoaiGiayChungNhan"), "Sheet danh mục phải còn");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Chủ dự án chốt: màn VietBD KHÔNG dùng chung bất cứ thứ gì của màn iLIS, chỉ dùng chung hạ tầng
    /// gọi AI và upload Gemini theo cấu trúc chung của hệ thống. Test này khoá lại ràng buộc đó.
    /// </summary>
    private static void VietBdPipelineIsFullySeparatedFromIlisScreen()
    {
        // 1. ViewModel không được chạm vào bất kỳ kiểu nào của pipeline iLIS.
        var vmSource = File.ReadAllText(FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnVietBdViewModel.cs")));
        foreach (var ilisType in new[]
                 {
                     "INewGcnExtractService", "INewGcnExcelExporter", "NewGcnRunCacheService",
                     "NewGcnOptions", "NewGcnEnvelope", "NewGcnRecord", "OCR.Business.NewGcn"
                 })
            AssertFalse(vmSource.Contains(ilisType, StringComparison.Ordinal),
                $"Màn VietBD không được dùng {ilisType} của màn iLIS.");

        AssertTrue(vmSource.Contains("IVietBdGcnExtractService", StringComparison.Ordinal),
            "Màn VietBD phải dùng extract service riêng.");
        AssertTrue(vmSource.Contains("VietBdGcnRunCacheService", StringComparison.Ordinal),
            "Màn VietBD phải dùng cache service riêng.");
        AssertTrue(vmSource.Contains("VietBdGcnOptions", StringComparison.Ordinal),
            "Màn VietBD phải dùng options riêng.");
        AssertTrue(vmSource.Contains("IVietBdGcnExcelExporter", StringComparison.Ordinal),
            "Màn VietBD phải dùng exporter riêng.");
        AssertTrue(vmSource.Contains("Title => \"OCR GCN VietBD\"", StringComparison.Ordinal),
            "Tên hiển thị phải là OCR GCN VietBD.");
        AssertTrue(vmSource.Contains("GCN-VietBD-output_", StringComparison.Ordinal),
            "File Excel xuất ra phải có tên riêng.");

        // Hộp thoại mã xã: hỏi SAU hộp thoại cache, bắt buộc nhập, và giá trị đi thẳng vào exporter.
        var startStart = vmSource.IndexOf("private async Task StartAsync()", StringComparison.Ordinal);
        var startExportStart = vmSource.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(startStart >= 0 && startExportStart > startStart, "Expected GcnVietBd StartAsync before ExportAsync.");
        var startBody = vmSource[startStart..startExportStart];

        var cachePromptCall = startBody.IndexOf("_cachePrompt.AskAsync()", StringComparison.Ordinal);
        var maXaPromptCall = startBody.IndexOf("_maXaPrompt.AskAsync(", StringComparison.Ordinal);
        AssertTrue(cachePromptCall >= 0, "Cơ chế hỏi dùng cache cũ / quét lại phải còn nguyên.");
        AssertTrue(maXaPromptCall > cachePromptCall,
            "Hộp thoại mã xã phải hỏi SAU hộp thoại cache.");
        AssertTrue(startBody.Contains("if (string.IsNullOrWhiteSpace(maXa)) return;", StringComparison.Ordinal),
            "Bỏ trống / huỷ nhập mã xã thì không được chạy lô.");
        AssertTrue(startBody.Contains("ResetWorkspaceAsync", StringComparison.Ordinal),
            "Chọn quét lại vẫn phải xoá workspace cache cũ.");
        AssertTrue(vmSource.Contains("_excel.Write(snapshot, outPath, _opt.TemplateExcel, _maXa)", StringComparison.Ordinal),
            "Mã xã đã nhập phải được truyền xuống exporter lúc Export.");

        // Quota vẫn trừ theo SỐ THỬA (khuôn VietBD nở dòng theo MĐSD × chủ).
        var exportStart = vmSource.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(exportStart >= 0, "Expected GcnVietBd ExportAsync.");
        var exportBody = vmSource[exportStart..];
        var parcelCount = exportBody.IndexOf("var parcelCount = snapshot.Sum(CountDisplayRows);", StringComparison.Ordinal);
        var recordCredit = exportBody.IndexOf("RecordOcrCreditAsync(", StringComparison.Ordinal);
        AssertTrue(parcelCount >= 0 && recordCredit > parcelCount,
            "Quota phải tính theo số thửa trước khi ghi nhận, không dùng số dòng Excel.");

        // 2. Extract service riêng phải nạp prompt riêng, không đụng prompt/kiểu của iLIS.
        var extractSource = File.ReadAllText(FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR.Business", "VietBdGcn", "VietBdGcnExtractService.cs")));
        AssertTrue(extractSource.Contains("PROMPT_TRICH_XUAT_GCN_VIETBD.md", StringComparison.Ordinal),
            "Extract service của VietBD phải nạp prompt riêng.");
        AssertFalse(extractSource.Contains("OCR.Business.NewGcn", StringComparison.Ordinal),
            "Extract service của VietBD không được tham chiếu namespace NewGcn.");
        AssertFalse(extractSource.Contains("NewGcnEnvelope", StringComparison.Ordinal),
            "Extract service của VietBD không được dùng schema của iLIS.");

        // 3. Hạ tầng dùng chung thì vẫn phải dùng chung, không copy thêm bản riêng.
        AssertTrue(extractSource.Contains("IAiModelClient", StringComparison.Ordinal),
            "Vẫn dùng chung client gọi AI của hệ thống.");
        AssertTrue(extractSource.Contains("IGeminiFileApiService", StringComparison.Ordinal),
            "Vẫn dùng chung dịch vụ upload Gemini của hệ thống.");
        AssertTrue(vmSource.Contains("IGeminiUploadPipeline", StringComparison.Ordinal),
            "Vẫn dùng chung hàng đợi upload Gemini của hệ thống.");

        // 4. Cache hai màn phải nằm ở hai gốc khác nhau để JSON không lẫn schema.
        var vietBdOptions = new VietBdGcnOptions();
        var ilisOptions = new NewGcnOptions();
        AssertFalse(string.Equals(vietBdOptions.TempDir, ilisOptions.TempDir, StringComparison.OrdinalIgnoreCase),
            "Thư mục cache JSON của hai màn phải khác nhau.");
        AssertFalse(string.Equals(vietBdOptions.TemplateExcel, ilisOptions.TemplateExcel, StringComparison.OrdinalIgnoreCase),
            "Template Excel của hai màn phải khác nhau.");

        // 5. Menu hiển thị đúng tên mới.
        var shell = File.ReadAllText(FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "Views", "ShellPage.xaml")));
        AssertTrue(shell.Contains("Content=\"OCR GCN VietBD\" Tag=\"gcn-vietbd\"", StringComparison.Ordinal),
            "Menu phải hiển thị OCR GCN VietBD.");
        AssertTrue(shell.Contains("Content=\"OCR GCN iLIS\" Tag=\"gcn-new\"", StringComparison.Ordinal),
            "Menu màn cũ giữ tên iLIS và Tag gcn-new.");
    }

    /// <summary>
    /// Prompt VietBD phải là file riêng, đã bỏ bảng tra của địa phương (mã xã / đơn vị đo đạc Cô Tô)
    /// và dùng bảng mã loại GCN của Việt Bản Đồ — đây là các điểm chủ dự án yêu cầu điều chỉnh.
    /// </summary>
    private static void VietBdPromptDropsLocalLookupTablesAndUsesVietBdCodes()
    {
        var asm = typeof(VietBdGcnExcelExporter).Assembly;
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("PROMPT_TRICH_XUAT_GCN_VIETBD.md", StringComparison.OrdinalIgnoreCase));
        AssertTrue(resName is not null, "Prompt VietBD phải được nhúng làm EmbeddedResource.");

        using var stream = asm.GetManifestResourceStream(resName!)!;
        using var reader = new StreamReader(stream);
        var prompt = reader.ReadToEnd();

        AssertFalse(prompt.Contains("BẢNG MÃ XÃ", StringComparison.Ordinal),
            "Prompt VietBD phải bỏ bảng mã xã của địa phương.");
        AssertFalse(prompt.Contains("BẢNG ĐƠN VỊ ĐO ĐẠC", StringComparison.Ordinal),
            "Prompt VietBD phải bỏ bảng đơn vị đo đạc của địa phương.");
        AssertFalse(prompt.Contains("Cô Tô", StringComparison.Ordinal),
            "Prompt VietBD không được còn dữ liệu riêng của huyện Cô Tô.");

        AssertTrue(prompt.Contains("BẢNG LOẠI GCN VIỆT BẢN ĐỒ", StringComparison.Ordinal),
            "Prompt VietBD phải dùng bảng loại GCN của Việt Bản Đồ.");
        AssertTrue(prompt.Contains("ma_loai_gcn", StringComparison.Ordinal),
            "Prompt VietBD phải yêu cầu xuất thẳng mã loại GCN.");
        AssertTrue(prompt.Contains("\"xa\": \"string|null\"", StringComparison.Ordinal)
                   && prompt.Contains("\"huyen\": \"string|null\"", StringComparison.Ordinal)
                   && prompt.Contains("\"tinh\": \"string|null\"", StringComparison.Ordinal),
            "Prompt VietBD phải tách sẵn xã/huyện/tỉnh thành ba trường.");
        AssertTrue(prompt.Contains("loai_thua_dat", StringComparison.Ordinal),
            "Prompt VietBD phải trả loại thửa đất cho cột CH.");
        AssertTrue(prompt.Contains("BẢNG LOẠI NHÀ RIÊNG LẺ", StringComparison.Ordinal),
            "Prompt VietBD phải có bảng mã loại nhà riêng lẻ cho cột DE.");
        AssertTrue(prompt.Contains("ma_quyen_so_huu", StringComparison.Ordinal)
                   && prompt.Contains("`0` nếu là **sở hữu riêng**, `1` nếu là **sở hữu chung**", StringComparison.Ordinal),
            "Prompt VietBD phải nêu rõ quy tắc sở hữu riêng=0 / chung=1 cho cột DG.");
        AssertTrue(prompt.Contains("\"nha_o\"", StringComparison.Ordinal),
            "Schema đầu ra của prompt VietBD phải có khối nha_o.");

        // Cả HAI prompt phải mô tả đúng cấu trúc mặt giấy và tách ba mục ghi chú / thay đổi.
        foreach (var (name, text) in new[] { ("VietBD", prompt), ("iLIS", ReadIlisPrompt(asm)) })
        {
            AssertTrue(text.Contains("MẪU CŨ — 4 mặt giấy", StringComparison.Ordinal),
                $"Prompt {name} phải mô tả mẫu cũ gồm 4 mặt giấy.");
            AssertTrue(text.Contains("MẪU QR (2025 trở đi) — chỉ 2 mặt giấy", StringComparison.Ordinal),
                $"Prompt {name} phải mô tả mẫu QR chỉ có 2 mặt giấy.");
            AssertTrue(text.Contains("mặt 4 | mặt 1", StringComparison.Ordinal)
                       && text.Contains("mặt 2 | mặt 3", StringComparison.Ordinal),
                $"Prompt {name} phải nêu cả hai kiểu ghép mặt trong file scan.");
            AssertTrue(text.Contains("kéo dài từ mặt 3 sang mặt 4", StringComparison.Ordinal),
                $"Prompt {name} phải nói rõ bảng Những thay đổi kéo dài từ mặt 3 sang mặt 4.");
            AssertTrue(text.Contains("ghi_chu_trang_1", StringComparison.Ordinal),
                $"Prompt {name} phải có trường ghi_chu_trang_1 riêng.");
            AssertTrue(text.Contains("MẶT 4", StringComparison.Ordinal),
                $"Prompt {name} phải chỉ đúng mã vạch nằm ở mặt 4.");
        }

        // Prompt của màn iLIS cũng phải sạch dữ liệu riêng của một địa phương (chủ dự án yêu cầu
        // lược bỏ đặc trưng tỉnh Cô Tô ở CẢ HAI màn).
        var ilisRes = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("PROMPT_TRICH_XUAT_GCN.md", StringComparison.OrdinalIgnoreCase)
                                 && !n.EndsWith("PROMPT_TRICH_XUAT_GCN_VIETBD.md", StringComparison.OrdinalIgnoreCase));
        AssertTrue(ilisRes is not null, "Prompt iLIS phải còn nguyên.");
        using var ilisStream = asm.GetManifestResourceStream(ilisRes!)!;
        using var ilisReader = new StreamReader(ilisStream);
        var ilisPrompt = ilisReader.ReadToEnd();
        AssertFalse(ilisPrompt.Contains("BẢNG MÃ XÃ", StringComparison.Ordinal),
            "Prompt iLIS phải bỏ bảng mã xã của địa phương.");
        AssertFalse(ilisPrompt.Contains("BẢNG ĐƠN VỊ ĐO ĐẠC", StringComparison.Ordinal),
            "Prompt iLIS phải bỏ bảng đơn vị đo đạc của địa phương.");
        AssertFalse(ilisPrompt.Contains("Cô Tô", StringComparison.Ordinal),
            "Prompt iLIS không được còn dữ liệu riêng của huyện Cô Tô.");
        // Prompt iLIS vẫn phải giữ bảng loại GCN RIÊNG của nó (tên nguyên văn, khác bảng mã của VietBD).
        AssertTrue(ilisPrompt.Contains("BẢNG LOẠI GCN", StringComparison.Ordinal),
            "Prompt iLIS vẫn phải có bảng loại GCN của riêng nó.");
        AssertFalse(ilisPrompt.Contains("BẢNG LOẠI GCN VIỆT BẢN ĐỒ", StringComparison.Ordinal),
            "Prompt iLIS không được dùng bảng mã loại GCN của VietBD.");
    }

    private static string ReadIlisPrompt(Assembly asm)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("PROMPT_TRICH_XUAT_GCN.md", StringComparison.OrdinalIgnoreCase)
                                 && !n.EndsWith("PROMPT_TRICH_XUAT_GCN_VIETBD.md", StringComparison.OrdinalIgnoreCase));
        AssertTrue(name is not null, "Prompt iLIS phải còn nguyên.");
        using var stream = asm.GetManifestResourceStream(name!)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
