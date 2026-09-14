using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using OCR.Business.Configuration;
using OCR.Business.DocxVbd;
using OCR.Business.Models;
using OCR.Business.VietBdGcn;

namespace OCR.Business.Tests;

/// <summary>Bộ test màn "Convert docx to Excel VBD" — chạy riêng bằng --docx-vbd-only.</summary>
internal static class DocxVbdTests
{
    public static void Run()
    {
        TestOptionsDefaults();
        TestSoHieuRules();
        TestNgayVaThoiHanRules();
        TestClassifyLoaiGcn();
        TestSplitDiaChi();
        TestReaderParsesPages();
        TestMapperBuildsEnvelope();
        TestEndToEndExcel();
        Console.WriteLine("All Convert docx to Excel VBD tests passed.");
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{name}: mong đợi [{expected}] nhưng nhận [{actual}]");
    }

    private static void AssertTrue(bool condition, string name)
    {
        if (!condition) throw new Exception($"{name}: điều kiện sai");
    }

    private static void TestOptionsDefaults()
    {
        var opt = new DocxVbdOptions();
        AssertEqual(Path.Combine("Assets", "Temp", "Excel_Template_VietBD.xlsx"), opt.TemplateExcel,
            "DocxVbdOptions.TemplateExcel mặc định (dùng chung template màn VietBD)");

        // KHÔNG được ném exception (mọi Load* của AppSettingsLoader đều nuốt lỗi).
        var loaded = AppSettingsLoader.LoadDocxVbd();
        if (loaded is null) throw new Exception("LoadDocxVbd trả null.");
    }

    private static void TestSoHieuRules()
    {
        string So(string raw, List<string> w) => DocxVbdEnvelopeMapper.ExtractSoHieu(raw, "số thửa", "Dòng thửa 1", w);

        var none = new List<string>();
        AssertEqual("49", So("Lô 49", none), "'Lô 49' chỉ lấy số");
        AssertEqual("7", So("K7", none), "'K7' chỉ lấy số");
        AssertEqual("9", So("Khoảnh 9", none), "'Khoảnh 9' chỉ lấy số");
        AssertEqual("1", So("1 (a)", none), "'1 (a)' một số duy nhất");
        AssertEqual("00", So("00", none), "'00' giữ nguyên số 0 đầu");
        AssertEqual("6", So("Đ6", none), "'Đ6' chỉ lấy số");
        AssertEqual("", So("", none), "ô trống → trống");
        AssertEqual(0, none.Count, "các ca một số KHÔNG sinh cảnh báo");

        var w1 = new List<string>();
        AssertEqual("13,14", So("13,14", w1), "nhiều số giữ cả dãy (dấu phẩy)");
        AssertEqual("17; 11", So("Lô 17; 11", w1), "nhiều số bỏ chữ giữ dãy");
        AssertEqual("78-79-80", So("78-79-80", w1), "nhiều số giữ dãy (gạch ngang)");
        AssertEqual(3, w1.Count, "mỗi ô nhiều số phải có cảnh báo");

        var w2 = new List<string>();
        AssertEqual("A", So("A", w2), "không có chữ số → giữ nguyên văn");
        AssertEqual("MTĐĐC 06-2017", So("MTĐĐC 06-2017", w2), "mảnh trích đo → giữ nguyên văn");
        AssertEqual(2, w2.Count, "không có số / mảnh trích đo phải có cảnh báo");
    }

    private static void TestNgayVaThoiHanRules()
    {
        AssertEqual(("30/05/2012", true), DocxVbdEnvelopeMapper.NormalizeNgay("30/5/2012"), "ngày d/M/yyyy → dd/MM/yyyy");
        AssertEqual(("04/05/1999", true), DocxVbdEnvelopeMapper.NormalizeNgay("4/5/1999"), "ngày thiếu số 0 đầu");
        AssertEqual(("31/02/2000", false), DocxVbdEnvelopeMapper.NormalizeNgay("31/02/2000"), "ngày không tồn tại → giữ nguyên, không phải ngày");
        AssertEqual(("không rõ", false), DocxVbdEnvelopeMapper.NormalizeNgay("không rõ"), "chuỗi lạ → giữ nguyên");
        AssertEqual(("", false), DocxVbdEnvelopeMapper.NormalizeNgay("  "), "trắng → trống");

        AssertEqual("31/12/2060", DocxVbdEnvelopeMapper.NormalizeThoiHan("Đến ngày 31/12/2060"), "'Đến ngày X' tách mốc ngày");
        AssertEqual("Đến năm 2063", DocxVbdEnvelopeMapper.NormalizeThoiHan("Đến năm 2063"), "'Đến năm' giữ nguyên văn");
        AssertEqual("Lâu dài", DocxVbdEnvelopeMapper.NormalizeThoiHan("Lâu dài"), "'Lâu dài' giữ nguyên văn");
    }

    private static void TestClassifyLoaiGcn()
    {
        var w = new List<string>();
        AssertEqual("2", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(new DateTime(1999, 7, 10), "", w), "trước 01/07/2004 → 2");
        AssertEqual("1", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(new DateTime(2004, 7, 1), "", w), "01/07/2004 → 1");
        AssertEqual("6", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(new DateTime(2012, 5, 30), "BI 999230", w), "2012 → 6");
        AssertEqual("11", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(new DateTime(2014, 7, 1), "", w), "01/07/2014 → 11");
        AssertEqual("98", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(new DateTime(2025, 1, 1), "", w), "từ 2025 → 98");
        AssertEqual(0, w.Count, "có ngày thì không cảnh báo");

        AssertEqual("1", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(null, "AA 123456", w), "thiếu ngày, serial 2 chữ A → 1");
        AssertEqual("2", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(null, "X 123456", w), "thiếu ngày, serial 1 chữ → 2");
        var wB = new List<string>();
        AssertEqual("11", DocxVbdEnvelopeMapper.ClassifyLoaiGcn(null, "BI 999230", wB), "thiếu ngày, serial B → tạm 11");
        AssertEqual(1, wB.Count, "serial B thiếu ngày phải cảnh báo 6/11");
        var wNone = new List<string>();
        AssertEqual(null, DocxVbdEnvelopeMapper.ClassifyLoaiGcn(null, "", wNone), "thiếu cả hai → null");
        AssertEqual(1, wNone.Count, "thiếu cả hai phải cảnh báo");
    }

    private static void TestSplitDiaChi()
    {
        var owner = new VietBdGcnOwner();
        DocxVbdEnvelopeMapper.SplitDiaChi("Thôn Nà Ó, xã An Lạc, huyện Sơn Động, tỉnh Bắc Giang", owner);
        AssertEqual("Thôn Nà Ó", owner.to_dan_pho, "thôn → tổ dân phố");
        AssertEqual("xã An Lạc", owner.xa, "tách xã");
        AssertEqual("huyện Sơn Động", owner.huyen, "tách huyện");
        AssertEqual("tỉnh Bắc Giang", owner.tinh, "tách tỉnh");

        var owner2 = new VietBdGcnOwner();
        DocxVbdEnvelopeMapper.SplitDiaChi("Khu 1, phường Trần Phú, thành phố Bắc Giang", owner2);
        AssertEqual("Khu 1", owner2.to_dan_pho, "khu → tổ dân phố");
        AssertEqual("phường Trần Phú", owner2.xa, "tách phường");
        AssertEqual("thành phố Bắc Giang", owner2.tinh, "thành phố cuối chuỗi → cấp tỉnh");

        var owner3 = new VietBdGcnOwner();
        DocxVbdEnvelopeMapper.SplitDiaChi("thị trấn An Châu, thành phố Bắc Giang, tỉnh Bắc Giang", owner3);
        AssertEqual("thị trấn An Châu", owner3.xa, "tách thị trấn");
        AssertEqual("thành phố Bắc Giang", owner3.huyen, "thành phố có tỉnh phía sau → cấp huyện");
        AssertEqual("tỉnh Bắc Giang", owner3.tinh, "tỉnh cuối chuỗi");
    }

    // ---------- Dựng docx tối thiểu trong bộ nhớ ----------

    private static string Cell(string text) =>
        $"<w:tc><w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p></w:tc>";

    private static string Row(params string[] cells) =>
        $"<w:tr>{string.Concat(cells.Select(Cell))}</w:tr>";

    private static string Paragraph(string text) =>
        $"<w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p>";

    /// <summary>Bảng một trang sổ chuẩn: mục I (1 dòng người), mục II (header + dòng dữ liệu), mục III.</summary>
    private static string PageTable(string nguoiCell, IEnumerable<string[]> parcelRows, IEnumerable<string[]> bienDongRows)
    {
        var sb = new StringBuilder();
        sb.Append("<w:tbl>");
        // Header mục I cố tình TÁCH RUN GIỮA TỪ như file thật ("NG Ư ỜI ... Đ ẤT").
        sb.Append("<w:tr><w:tc><w:p><w:r><w:t xml:space=\"preserve\">I - NG</w:t></w:r>" +
                  "<w:r><w:t xml:space=\"preserve\">Ư ỜI SỬ DỤNG  Đ ẤT</w:t></w:r></w:p></w:tc></w:tr>");
        sb.Append(Row(nguoiCell));
        sb.Append(Row("II - THỬA  Đ ẤT"));
        sb.Append(Row("Ngày tháng năm vào sổ", "Số thứ tự thửa đất", "Số thứ tự tờ bản đồ", "Diện tích sử dụng (m2)",
            "Mục đích sử dụng", "Thời hạn sử dụng", "Nguồn gốc sử dụng", "Số phát hành GCN QSDĐ",
            "Số vào sổ cấp GCN QSDĐ", "Ghi chú"));
        sb.Append(Row("", "", "", "Riêng", "Chung", "", "", "", "", "", ""));
        sb.Append(Row("1", "2", "3", "5", "6", "7", "8", "9", "10", "11", ""));
        foreach (var cells in parcelRows) sb.Append(Row(cells));
        sb.Append(Row("", "", "", "", "", "", "", "", "", "", "")); // dòng trống phải bị bỏ qua
        sb.Append(Row("III - NHỮNG THAY  Đ ỔI TRONG QUÁ TRÌNH SỬ DỤNG  Đ ẤT VÀ GHI CHÚ"));
        sb.Append(Row("Số thứ tự thửa đất", "Ngày tháng năm", "Nội dung ghi chú hoặc biến động và căn cứ pháp lý"));
        foreach (var cells in bienDongRows) sb.Append(Row(cells));
        sb.Append(Row("", "", "")); // dòng trống mục III cũng bị bỏ qua
        sb.Append("</w:tbl>");
        return sb.ToString();
    }

    private static MemoryStream BuildDocx(string bodyXml)
    {
        var documentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            $"<w:body>{bodyXml}</w:body></w:document>";

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(documentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private const string NguoiVoChong =
        "1. Họ và tên:    Chu Văn An                       Năm sinh:  1974 " +
        "1.1. Số CMTND/CCCD/CMQĐ/HC:  121471282 " +
        "1.2. Địa chỉ:  Thôn Nà Ó, xã An Lạc, huyện Sơn Động, tỉnh Bắc Giang " +
        "2. Họ và tên:  Nguyễn Thị Thực       Năm sinh:  " +
        "2.1. Số CMTND/CCCD/CMQĐ/HC:  2.2. Địa chỉ:  Thôn Nà Ó, xã An Lạc, huyện Sơn Động, tỉnh Bắc Giang";

    private const string NguoiCaNhan =
        "1. Họ và tên:    Dương Trung An     Năm sinh:  1991 " +
        "1.1. Số CMTND/CCCD/CMQĐ/HC:  082158357082 " +
        "1.2. Địa chỉ:  Thôn Nà Trắng, xã An Lạc, huyện Sơn Động, tỉnh Bắc Giang " +
        "2. Họ và tên:                       Năm sinh:  2.1. Số CMTND/CCCD/CMQĐ/HC:  2.2. Địa chỉ: ";

    private static MemoryStream BuildSampleDocx()
    {
        var body = new StringBuilder();
        body.Append(Paragraph("(Tiếp theo trang số: …)      Trang số:   08"));
        body.Append(PageTable(
            NguoiVoChong,
            new[]
            {
                new[] { "30/5/2012", "Lô 49", "Khoảnh 9", "11.700,0", "", "RSX", "Đến ngày 31/12/2060", "DG-KTT", "BI 999230", "00785", "Q5-2012-T785" }
            },
            new[]
            {
                new[] { "49", "01/01/2015", "Chuyển nhượng cho ông A theo hồ sơ số 123" }
            }));
        body.Append(Paragraph("Chuyển tiếp trang số: …"));
        body.Append(Paragraph("(Tiếp theo trang số: …)      Trang số:   09"));
        body.Append(PageTable(
            NguoiCaNhan,
            new[]
            {
                new[] { "10/7/1999", "13,14", "14", "240,0", "", "LUK", "Đến năm 2063", "TA-CNQ-CTT", "", "00741", "Q4-1999 T21" }
            },
            Array.Empty<string[]>()));
        // Trang 10: MỘT thửa NHIỀU mục đích sử dụng — dòng cha mang tổng diện tích (MĐSD trống),
        // hai dòng nối tiếp ngay dưới chỉ có diện tích/MĐSD/thời hạn (thực tế gặp ở sổ An Lạc trang 14).
        body.Append(Paragraph("(Tiếp theo trang số: …)      Trang số:   10"));
        body.Append(PageTable(
            NguoiCaNhan,
            new[]
            {
                new[] { "04/12/2013", "00", "00", "5.100,5", "", "", "", "CNQ-CTT", "BR 314469", "00062", "Q1-2013-T62" },
                new[] { "", "", "", "360,0", "", "ONT", "Lâu dài", "", "", "", "" },
                new[] { "", "", "", "4.740,5", "", "CLN", "Đến năm 2043", "", "", "", "" }
            },
            Array.Empty<string[]>()));
        // Bảng KHÔNG phải trang sổ (thiếu marker mục I/II) phải bị bỏ qua.
        body.Append($"<w:tbl>{Row("Bảng thống kê khác", "không liên quan")}</w:tbl>");
        return BuildDocx(body.ToString());
    }

    private static void TestReaderParsesPages()
    {
        using var docx = BuildSampleDocx();
        var pages = QuyenSoDocxReader.Read(docx);

        AssertEqual(3, pages.Count, "chỉ 3 bảng đúng mẫu thành trang sổ (bảng lạ bị bỏ)");

        var p1 = pages[0];
        AssertEqual("08", p1.PageLabel, "trang 1 lấy đúng 'Trang số' phía trước bảng");
        AssertEqual(2, p1.NguoiSuDung.Count, "trang 1 có 2 người (vợ chồng)");
        AssertEqual("Chu Văn An", p1.NguoiSuDung[0].HoTen, "tên người 1");
        AssertEqual("1974", p1.NguoiSuDung[0].NamSinh, "năm sinh người 1");
        AssertEqual("121471282", p1.NguoiSuDung[0].SoGiayTo, "số giấy tờ người 1");
        AssertEqual("Nguyễn Thị Thực", p1.NguoiSuDung[1].HoTen, "tên người 2");
        AssertEqual(1, p1.ThuaDat.Count, "trang 1 có 1 dòng thửa (dòng trống + header bị bỏ)");
        var r1 = p1.ThuaDat[0];
        AssertEqual("30/5/2012", r1.NgayVaoSo, "ngày vào sổ nguyên văn");
        AssertEqual("Lô 49", r1.SoThua, "số thửa nguyên văn");
        AssertEqual("Khoảnh 9", r1.SoTo, "số tờ nguyên văn");
        AssertEqual("11.700,0", r1.DienTichRieng, "diện tích riêng");
        AssertEqual("RSX", r1.MucDichSuDung, "mục đích sử dụng");
        AssertEqual("BI 999230", r1.SoPhatHanh, "số phát hành");
        AssertEqual("00785", r1.SoVaoSo, "số vào sổ");
        AssertEqual("Q5-2012-T785", r1.GhiChu, "ghi chú mục II đọc được (sẽ bị bỏ khi map)");
        AssertEqual(1, p1.BienDong.Count, "trang 1 có 1 dòng biến động");
        AssertEqual("49", p1.BienDong[0].SoThua, "biến động: số thửa");
        AssertEqual(0, p1.CanhBaoDoc.Count, "trang chuẩn không có cảnh báo đọc");

        var p2 = pages[1];
        AssertEqual("09", p2.PageLabel, "trang 2 lấy đúng 'Trang số'");
        AssertEqual(1, p2.NguoiSuDung.Count, "trang 2 chỉ có người 1 (mục 2. trống)");
        AssertEqual(1, p2.ThuaDat.Count, "trang 2 có 1 dòng thửa");
        AssertEqual(0, p2.BienDong.Count, "trang 2 không có biến động");

        var p3 = pages[2];
        AssertEqual("10", p3.PageLabel, "trang 3 lấy đúng 'Trang số'");
        AssertEqual(3, p3.ThuaDat.Count, "trang 3 đọc thô 3 dòng (1 cha + 2 nối tiếp, gom ở tầng map)");
    }

    private static void TestMapperBuildsEnvelope()
    {
        using var docx = BuildSampleDocx();
        var pages = QuyenSoDocxReader.Read(docx);
        var env1 = DocxVbdEnvelopeMapper.Map(pages[0], "Q1 An Lac.docx");
        var env2 = DocxVbdEnvelopeMapper.Map(pages[1], "Q1 An Lac.docx");

        AssertEqual("Q1 An Lac.docx - Trang 08", env1.ten_file, "ten_file gồm tên docx + trang");
        AssertEqual("vo_chong", env1.thong_tin_gcn.loai_quan_he, "2 người → vợ chồng");
        AssertEqual("BI 999230", env1.thong_tin_gcn.so_serial, "serial lấy từ cột số phát hành");
        AssertEqual("6", env1.thong_tin_gcn.ma_loai_gcn, "ngày 30/05/2012 → mã loại GCN 6");
        AssertEqual(0, env1.thong_tin_gcn.canh_bao!.Count, "trang chuẩn không có cảnh báo");
        var chu = env1.thong_tin_gcn.chu_su_dung_chi_tiet![0];
        AssertEqual("CMND", chu.loai_giay_to, "9 số → CMND");
        AssertEqual("Thôn Nà Ó", chu.to_dan_pho, "địa chỉ tách thôn");
        AssertEqual("xã An Lạc", chu.xa, "địa chỉ tách xã");
        var dong1 = env1.danh_sach_dong[0];
        AssertEqual("49", dong1.td_so_thua, "số thửa chỉ lấy số");
        AssertEqual("9", dong1.td_so_to, "số tờ chỉ lấy số");
        AssertEqual("30/05/2012", dong1.ky_ngay_ky_gcn, "ngày vào sổ → ngày cấp chuẩn hoá");
        AssertEqual("00785", dong1.ky_so_vao_so, "số vào sổ giữ số 0 đầu");
        AssertEqual("A", dong1.loai_thua_dat, "loại thửa mặc định A");
        var mdsd1 = dong1.muc_dich_su_dung![0];
        AssertEqual("RSX", mdsd1.ma_mdsd, "mã MĐSD giữ nguyên");
        AssertEqual("31/12/2060", mdsd1.thoi_han_su_dung, "'Đến ngày' tách mốc ngày");
        AssertEqual("DG-KTT", mdsd1.ma_ngsd, "nguồn gốc thuộc danh mục ghi bình thường");
        AssertEqual(1, env1.thong_tin_gcn.thong_tin_thay_doi!.Count, "mục III vào thong_tin_thay_doi");
        AssertTrue(env1.thong_tin_gcn.thong_tin_thay_doi![0].Contains("Thửa 49", StringComparison.Ordinal)
                   && env1.thong_tin_gcn.thong_tin_thay_doi![0].Contains("Chuyển nhượng", StringComparison.Ordinal),
            "nội dung biến động giữ thửa + nội dung");

        AssertEqual("ca_nhan", env2.thong_tin_gcn.loai_quan_he, "1 người → cá nhân");
        AssertEqual("CCCD", env2.thong_tin_gcn.chu_su_dung_chi_tiet![0].loai_giay_to, "12 số → CCCD");
        AssertEqual("", env2.thong_tin_gcn.so_serial, "trang không có serial → trống");
        AssertEqual("2", env2.thong_tin_gcn.ma_loai_gcn, "ngày 1999 → mã loại GCN 2");
        AssertEqual("13,14", env2.danh_sach_dong[0].td_so_thua, "nhiều số thửa giữ cả dãy");
        AssertEqual("Đến năm 2063", env2.danh_sach_dong[0].muc_dich_su_dung![0].thoi_han_su_dung,
            "'Đến năm' giữ nguyên văn");
        var canhBao2 = env2.thong_tin_gcn.canh_bao!;
        AssertTrue(canhBao2.Any(c => c.Contains("13,14", StringComparison.Ordinal)), "cảnh báo nhiều số thửa");
        AssertTrue(canhBao2.Any(c => c.Contains("TA-CNQ-CTT", StringComparison.Ordinal)),
            "cảnh báo mã nguồn gốc ngoài danh mục");

        // Trang 10: một thửa nhiều MĐSD — 2 dòng nối tiếp gom về thửa cha.
        var env3 = DocxVbdEnvelopeMapper.Map(pages[2], "Q1 An Lac.docx");
        AssertEqual(1, env3.danh_sach_dong.Count, "1 cha + 2 nối tiếp = 1 thửa");
        AssertEqual(1, env3.thong_tin_gcn.so_luong_thua_dat_doc_duoc, "số thửa đọc được đếm theo thửa, không theo dòng thô");
        var thua3 = env3.danh_sach_dong[0];
        AssertEqual("5.100,5", thua3.td_tong_dien_tich, "dòng cha giữ TỔNG diện tích");
        AssertEqual("00062", thua3.ky_so_vao_so, "số vào sổ lấy từ dòng cha");
        AssertEqual(2, thua3.muc_dich_su_dung!.Count, "MĐSD trống ở dòng cha → chỉ 2 mục từ dòng nối tiếp");
        AssertEqual("ONT", thua3.muc_dich_su_dung![0].ma_mdsd, "MĐSD 1 từ dòng nối tiếp");
        AssertEqual("360,0", thua3.muc_dich_su_dung![0].dien_tich, "diện tích riêng của MĐSD 1");
        AssertEqual("CNQ-CTT", thua3.muc_dich_su_dung![0].ma_ngsd, "nguồn gốc dòng nối tiếp thừa hưởng từ dòng cha");
        AssertEqual("CLN", thua3.muc_dich_su_dung![1].ma_mdsd, "MĐSD 2 từ dòng nối tiếp");
        AssertEqual("4.740,5", thua3.muc_dich_su_dung![1].dien_tich, "diện tích riêng của MĐSD 2");
        AssertTrue(!env3.thong_tin_gcn.canh_bao!.Any(c => c.Contains("thiếu mục đích", StringComparison.Ordinal)),
            "dòng cha trống MĐSD nhưng có dòng nối tiếp thì KHÔNG cảnh báo thiếu MĐSD");
    }

    private static void TestEndToEndExcel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("KeKhaiDangKy");
                template.SaveAs(templatePath);
            }

            using var docx = BuildSampleDocx();
            var pages = QuyenSoDocxReader.Read(docx);
            var envelopes = pages.Select(p => DocxVbdEnvelopeMapper.Map(p, "Q1 An Lac.docx")).ToList();

            var exporter = new VietBdGcnExcelExporter();
            int rows = exporter.Write(envelopes, outputPath, templatePath, "11407");
            AssertEqual(4, rows, "trang 08 + trang 09 mỗi trang 1 dòng, trang 10 nở 2 dòng theo 2 MĐSD");

            using var output = new XLWorkbook(outputPath);
            var ws = output.Worksheet("KeKhaiDangKy");

            // Dòng 5 — trang 08 (vợ chồng, đủ dữ liệu).
            AssertEqual("11407", ws.Cell(5, "B").Value.ToString(), "cột B = mã xã nhập tay");
            AssertEqual("BI 999230", ws.Cell(5, "N").Value.ToString(), "cột N = số phát hành");
            AssertEqual("00785", ws.Cell(5, "O").Value.ToString(), "cột O = số vào sổ");
            AssertEqual("30/05/2012", ws.Cell(5, "R").Value.ToString(), "cột R = ngày vào sổ (ngày cấp)");
            AssertEqual("30/05/2012", ws.Cell(5, "I").Value.ToString(), "cột I = cùng ngày cấp");
            AssertEqual("6", ws.Cell(5, "W").Value.ToString(), "cột W = mã loại GCN suy từ ngày");
            AssertEqual("Vợ chồng", ws.Cell(5, "Z").Value.ToString(), "cột Z = Vợ chồng");
            AssertEqual("Chu Văn An", ws.Cell(5, "AB").Value.ToString(), "khối CHU_ = người 1");
            AssertEqual("CMND", ws.Cell(5, "AR").Value.ToString(), "loại giấy tờ người 1");
            AssertEqual("121471282", ws.Cell(5, "AS").Value.ToString(), "số giấy tờ người 1");
            AssertEqual("Nguyễn Thị Thực", ws.Cell(5, "AV").Value.ToString(), "khối VC_ = người 2");
            AssertEqual("A", ws.Cell(5, "CH").Value.ToString(), "loại thửa A");
            AssertEqual("49", ws.Cell(5, "CI").Value.ToString(), "cột CI = số thửa chỉ lấy số");
            AssertEqual("9", ws.Cell(5, "CJ").Value.ToString(), "cột CJ = số tờ chỉ lấy số");
            AssertEqual("11.700,0", ws.Cell(5, "CN").Value.ToString(), "cột CN = diện tích");
            AssertEqual("RSX", ws.Cell(5, "CU").Value.ToString(), "cột CU = mã MĐSD");
            AssertEqual("31/12/2060", ws.Cell(5, "CY").Value.ToString(), "cột CY = thời hạn dạng ngày");
            AssertEqual("31/12/2060", ws.Cell(5, "CZ").Value.ToString(), "cột CZ = thời hạn");
            AssertEqual("DG-KTT", ws.Cell(5, "DA").Value.ToString(), "cột DA = nguồn gốc");
            AssertEqual("Q1 An Lac.docx - Trang 08", ws.Cell(5, "FE").Value.ToString(), "cột FE = tên file + trang");
            AssertTrue(ws.Cell(5, "FH").Value.ToString().Contains("Chuyển nhượng", StringComparison.Ordinal),
                "cột FH = biến động mục III");
            AssertEqual("", ws.Cell(5, "FG").Value.ToString(), "trang chuẩn không có cảnh báo FG");
            // Cột "Ghi chú" mục II (Q5-2012-T785) phải bị BỎ — không xuất hiện ở hai cột ghi chú.
            AssertEqual("", ws.Cell(5, "X").Value.ToString(), "ghi chú trang 1 trống");
            AssertEqual("", ws.Cell(5, "Y").Value.ToString(), "ghi chú trang 2 trống (cột Ghi chú mục II bị bỏ)");

            // Dòng 6 — trang 09 (cá nhân, thiếu serial, nhiều số thửa, nguồn gốc lạ).
            AssertEqual("Cá nhân", ws.Cell(6, "Z").Value.ToString(), "cột Z = Cá nhân");
            AssertEqual("", ws.Cell(6, "N").Value.ToString(), "serial trống giữ trống");
            AssertEqual("13,14", ws.Cell(6, "CI").Value.ToString(), "nhiều số thửa giữ cả dãy");
            AssertEqual("TA-CNQ-CTT", ws.Cell(6, "DA").Value.ToString(), "nguồn gốc lạ ghi nguyên văn");
            AssertEqual("", ws.Cell(6, "CY").Value.ToString(), "'Đến năm 2063' không phải mốc ngày → CY trống");
            AssertEqual("Đến năm 2063", ws.Cell(6, "CZ").Value.ToString(), "CZ giữ nguyên văn thời hạn");
            var fg = ws.Cell(6, "FG").Value.ToString();
            AssertTrue(fg.Contains("13,14", StringComparison.Ordinal), "FG có cảnh báo nhiều số thửa");
            AssertTrue(fg.Contains("TA-CNQ-CTT", StringComparison.Ordinal), "FG có cảnh báo nguồn gốc lạ");

            // Dòng 7-8 — trang 10: MỘT thửa nở 2 dòng theo 2 MĐSD, tổng diện tích lặp lại ở CN.
            AssertEqual("BR 314469", ws.Cell(7, "N").Value.ToString(), "serial dòng cha cho cả cụm");
            AssertEqual("00062", ws.Cell(7, "O").Value.ToString(), "số vào sổ dòng MĐSD 1");
            AssertEqual("00062", ws.Cell(8, "O").Value.ToString(), "số vào sổ lặp lại ở dòng MĐSD 2");
            AssertEqual("5.100,5", ws.Cell(7, "CN").Value.ToString(), "CN dòng 1 = tổng diện tích");
            AssertEqual("5.100,5", ws.Cell(8, "CN").Value.ToString(), "CN dòng 2 = tổng diện tích lặp lại");
            AssertEqual("ONT", ws.Cell(7, "CU").Value.ToString(), "CU dòng 1 = MĐSD 1");
            AssertEqual("CLN", ws.Cell(8, "CU").Value.ToString(), "CU dòng 2 = MĐSD 2");
            AssertEqual("360,0", ws.Cell(7, "CX").Value.ToString(), "CX dòng 1 = diện tích MĐSD 1");
            AssertEqual("4.740,5", ws.Cell(8, "CX").Value.ToString(), "CX dòng 2 = diện tích MĐSD 2");
            AssertEqual("CNQ-CTT", ws.Cell(7, "DA").Value.ToString(), "DA thừa hưởng nguồn gốc dòng cha");
            AssertEqual("CNQ-CTT", ws.Cell(8, "DA").Value.ToString(), "DA dòng 2 cũng thừa hưởng");
            AssertEqual("Lâu dài", ws.Cell(7, "CZ").Value.ToString(), "CZ dòng 1 = thời hạn MĐSD 1");
            AssertEqual("Đến năm 2043", ws.Cell(8, "CZ").Value.ToString(), "CZ dòng 2 = thời hạn MĐSD 2");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
