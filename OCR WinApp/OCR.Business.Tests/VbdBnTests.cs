using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OCR.Business.Configuration;
using OCR.Business.Models;

namespace OCR.Business.Tests;

/// <summary>Bộ test màn OCR GCN VBD-BN — chạy riêng bằng --vbd-bn-only.</summary>
internal static class VbdBnTests
{
    public static void Run()
    {
        TestOptionsDefaults();
        TestCccdEnvelopeParsing();
        TestFileClassifier();
        TestSerialMap();
        TestCccdExtractServiceCache();
        TestVbdBnExporter();
        Console.WriteLine("All VBD-BN tests passed.");
    }

    private static void TestOptionsDefaults()
    {
        var opt = new VbdBnOptions();
        AssertEqual(5, opt.Workers, "VbdBnOptions.Workers mặc định");
        AssertEqual(false, opt.OptimizeImages, "VbdBnOptions.OptimizeImages mặc định");
        AssertEqual(Path.Combine("Assets", "Temp", "Excel_Template_VietBD_BN.xlsx"), opt.TemplateExcel, "VbdBnOptions.TemplateExcel mặc định");
        AssertEqual("GCN", opt.GcnKeyword, "VbdBnOptions.GcnKeyword mặc định");
        AssertEqual("GTK", opt.GtkKeyword, "VbdBnOptions.GtkKeyword mặc định");
        if (!opt.TempDir.EndsWith("vbdbn-temp", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"TempDir phải kết thúc bằng vbdbn-temp, đang là: {opt.TempDir}");

        // KHÔNG được ném exception (mọi Load* của AppSettingsLoader đều nuốt lỗi).
        var loaded = AppSettingsLoader.LoadVbdBn();
        if (loaded is null) throw new Exception("LoadVbdBn trả null.");
    }

    internal static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{name}: mong đợi [{expected}] nhưng nhận [{actual}]");
    }

    internal static void AssertTrue(bool condition, string name)
    {
        if (!condition) throw new Exception($"{name}: điều kiện sai");
    }

    private static void TestCccdEnvelopeParsing()
    {
        var json = """
        {
          "danh_sach_giay_to": [
            {
              "loai_giay_to": "cccd", "so_giay_to": "012345678901", "ho_ten": "Nguyễn Văn A",
              "ngay_sinh": "01/02/1980", "gioi_tinh": "Nam", "quoc_tich": "Việt Nam",
              "que_quan": "Bắc Ninh", "noi_thuong_tru": "Phường X, Bắc Ninh",
              "ngay_cap": "15/03/2022", "noi_cap": "Cục CS QLHC về TTXH",
              "co_gia_tri_den": "01/02/2040", "trang": [3, 4],
              "do_tin_cay": "cao", "canh_bao": []
            },
            { "loai_giay_to": "cmnd", "so_giay_to": "123456789", "ho_ten": "Trần Thị B",
              "trang": [7], "do_tin_cay": "trung_binh",
              "canh_bao": ["ho_ten (trang 7): chữ mờ – đọc được: Trần Thị B"] }
          ]
        }
        """;
        var env = System.Text.Json.JsonSerializer.Deserialize<OCR.Business.VbdBn.CccdEnvelope>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        AssertEqual(2, env.danh_sach_giay_to.Count, "số giấy tờ parse được");
        AssertEqual("012345678901", env.danh_sach_giay_to[0].so_giay_to, "số CCCD");
        AssertEqual(2, env.danh_sach_giay_to[0].trang!.Count, "số trang của giấy 1");
        AssertEqual(null, env.danh_sach_giay_to[1].quoc_tich, "trường thiếu → null");

        // Mảng rỗng là hợp lệ (file GTK không có giấy tờ tuỳ thân) — KHÔNG phải lỗi.
        var empty = System.Text.Json.JsonSerializer.Deserialize<OCR.Business.VbdBn.CccdEnvelope>(
            """{ "danh_sach_giay_to": [] }""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        AssertEqual(0, empty.danh_sach_giay_to.Count, "danh sách rỗng hợp lệ");

        // Schema phải dựng được và là object (không ném).
        AssertTrue(OCR.Business.VbdBn.CccdResponseSchema.Instance is not null, "schema dựng được");
    }

    private static void TestFileClassifier()
    {
        var C = (string name) => OCR.Business.VbdBn.VbdBnFileClassifier.Classify(name, "GCN", "GTK");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gcn, C("123-GCN"), "tên chứa GCN");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gtk, C("123-GTK"), "tên chứa GTK");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gcn, C("GCN-GTK-gop"), "chứa cả hai → GCN");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gcn, C("ho-so-gcn-01"), "chữ thường");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gcn, C("giấy-Gcn-số-1"), "hoa thường lẫn + có dấu quanh keyword");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.Gtk, C("Giấy-tờ-khác-GTK-đợt-2"), "GTK giữa tên có dấu");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.None, C("don-dang-ky"), "không khớp → None");
        AssertEqual(OCR.Business.VbdBn.VbdBnFileKind.None, C(""), "tên rỗng → None");
    }

    private static void TestSerialMap()
    {
        string d1 = Path.Combine(Path.GetTempPath(), "vbdbn-test", "HoSo01");
        string d2 = Path.Combine(Path.GetTempPath(), "vbdbn-test", "HoSo02");
        string d3 = Path.Combine(Path.GetTempPath(), "vbdbn-test", "HoSo03");
        var map = OCR.Business.VbdBn.VbdBnSerialMap.Build(new (string, string?)[]
        {
            (Path.Combine(d1, "a-GCN.pdf"), "AB 123456"),
            (Path.Combine(d2, "b-GCN.pdf"), "CD 111111"),
            (Path.Combine(d2, "c-GCN.pdf"), "CD 222222"),
            (Path.Combine(d3, "d-GCN.pdf"), null),          // GCN không đọc được serial → bỏ qua
        });

        var r1 = OCR.Business.VbdBn.VbdBnSerialMap.Resolve(Path.Combine(d1, "a-GTK.pdf"), map);
        AssertEqual("AB 123456", r1.SerialJoined, "1 GCN cùng thư mục");
        AssertEqual(null, r1.Warning, "1 GCN không cảnh báo");

        var r2 = OCR.Business.VbdBn.VbdBnSerialMap.Resolve(Path.Combine(d2, "b-GTK.pdf"), map);
        AssertEqual("CD 111111; CD 222222", r2.SerialJoined, "nhiều GCN → nối ; ");
        AssertTrue(r2.Warning is not null, "nhiều GCN phải có cảnh báo");

        var r3 = OCR.Business.VbdBn.VbdBnSerialMap.Resolve(Path.Combine(d3, "d-GTK.pdf"), map);
        AssertEqual("", r3.SerialJoined, "thư mục không có serial → trống");
        AssertTrue(r3.Warning is not null, "không serial phải có cảnh báo");

        // GTK ở thư mục CHA của thư mục có GCN → KHÔNG lấy (chỉ cùng thư mục trực tiếp).
        var r4 = OCR.Business.VbdBn.VbdBnSerialMap.Resolve(
            Path.Combine(Path.GetTempPath(), "vbdbn-test", "x-GTK.pdf"), map);
        AssertEqual("", r4.SerialJoined, "thư mục cha không kế thừa serial của con");
    }

    private sealed class FakeAiClient : OCR.Business.Ai.IAiModelClient
    {
        public int Calls;
        public string Response = """{ "danh_sach_giay_to": [ { "loai_giay_to": "cccd", "so_giay_to": "012345678901", "trang": [1] } ] }""";
        public Task<string> GenerateJsonAsync(
            IReadOnlyList<object> content, int maxAttempts = 3, int? maxTokens = null,
            double? temperature = null, bool? reasoningEnabled = null, string? reasoningEffort = null,
            string? pdfParserEngine = null, bool enableRouterMetadata = false, int timeoutSeconds = 180,
            object? responseSchema = null, string? escalatedReasoningEffort = null,
            System.Threading.CancellationToken ct = default)
        {
            Calls++;
            // Khoá đúng hợp đồng content mà AiModelClient.ToGeminiParts thật sự đọc được (dict có khoá
            // "type") — nếu service thêm string/GeminiFileReference thô vào content (bug của skeleton
            // brief), test này phải FAIL thay vì âm thầm pass.
            ValidateContent(content);
            return Task.FromResult(Response);
        }

        private static void ValidateContent(IReadOnlyList<object> content)
        {
            if (content.Count < 2)
                throw new Exception($"content phải có ít nhất 2 phần tử (prompt + file), có {content.Count}");

            if (content[0] is not Dictionary<string, object> textPart)
                throw new Exception($"content[0] phải là Dictionary<string,object>, là {content[0]?.GetType().Name ?? "null"}");
            if (!textPart.TryGetValue("type", out var textType) || (textType as string) != "text")
                throw new Exception("content[0] phải có [\"type\"] == \"text\"");
            if (!textPart.TryGetValue("text", out var textValue) || textValue is not string textStr || textStr.Length == 0)
                throw new Exception("content[0][\"text\"] phải là chuỗi khác rỗng");

            if (content[1] is not Dictionary<string, object> filePart)
                throw new Exception($"content[1] phải là Dictionary<string,object>, là {content[1]?.GetType().Name ?? "null"}");
            if (!filePart.TryGetValue("type", out var fileType))
                throw new Exception("content[1] thiếu [\"type\"]");
            var fileTypeStr = fileType as string;
            if (fileTypeStr == "file_uri")
            {
                if (!filePart.TryGetValue("file_uri", out var fileUriObj) ||
                    fileUriObj is not Dictionary<string, string> fileUriDict)
                    throw new Exception("content[1][\"file_uri\"] phải là Dictionary<string,string>");
                if (!fileUriDict.ContainsKey("mime_type") || string.IsNullOrEmpty(fileUriDict["mime_type"]))
                    throw new Exception("content[1][\"file_uri\"][\"mime_type\"] thiếu/rỗng");
                if (!fileUriDict.ContainsKey("file_uri") || string.IsNullOrEmpty(fileUriDict["file_uri"]))
                    throw new Exception("content[1][\"file_uri\"][\"file_uri\"] thiếu/rỗng");
            }
            else if (fileTypeStr == "file")
            {
                if (!filePart.TryGetValue("file", out var fileObj) ||
                    fileObj is not Dictionary<string, string> fileDict)
                    throw new Exception("content[1][\"file\"] phải là Dictionary<string,string>");
                if (!fileDict.TryGetValue("file_data", out var fileData) ||
                    !fileData.StartsWith("data:application/pdf;base64,", StringComparison.Ordinal))
                    throw new Exception("content[1][\"file\"][\"file_data\"] phải bắt đầu bằng data:application/pdf;base64,");
            }
            else
            {
                throw new Exception($"content[1][\"type\"] phải là \"file_uri\" hoặc \"file\", là \"{fileTypeStr}\"");
            }
        }
    }

    private static void TestCccdExtractServiceCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vbdbn-test-extract-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var pdf = Path.Combine(dir, "ho-so-GTK.pdf");
            File.WriteAllBytes(pdf, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF" giả — service không parse PDF khi có uploadedFiles
            var cachePath = Path.Combine(dir, "cache", "ho-so-GTK.json");
            var fake = new FakeAiClient();
            var svc = new OCR.Business.VbdBn.CccdExtractService(fake);
            var uploaded = new List<OCR.Business.Ai.GeminiFileReference>
            {
                new("f1", "https://example.invalid/f1", "application/pdf")
            };

            var env1 = svc.ProcessFileAsync(pdf, null, default, cachePath, useCachedJson: true, uploaded).GetAwaiter().GetResult();
            AssertEqual(1, env1!.danh_sach_giay_to.Count, "lần 1 parse từ AI");
            AssertEqual(1, fake.Calls, "lần 1 gọi AI đúng 1 lượt");
            AssertTrue(File.Exists(cachePath), "đã ghi cache JSON");

            var env2 = svc.ProcessFileAsync(pdf, null, default, cachePath, useCachedJson: true, uploaded).GetAwaiter().GetResult();
            AssertEqual(1, env2!.danh_sach_giay_to.Count, "lần 2 đọc từ cache");
            AssertEqual(1, fake.Calls, "lần 2 KHÔNG gọi AI thêm");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private sealed class FakeVietBdExporter : OCR.Business.VietBdGcn.IVietBdGcnExcelExporter
    {
        public int Write(
            IEnumerable<VietBdGcnEnvelope> envelopes, string outputPath, string templatePath, string? maXa = null,
            Func<VietBdGcnEnvelope, string>? resolveTenFile = null)
        {
            // Giả lập bước 1 của exporter thật: tạo file đầu ra từ template.
            File.Copy(templatePath, outputPath, overwrite: true);
            return envelopes.Count();
        }
    }

    private static void TestVbdBnExporter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vbdbn-test-export-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "template.xlsx");
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                wb.AddWorksheet("KeKhaiDangKy").Cell(4, 1).Value = "hdr";
                wb.AddWorksheet("ThongTinCCCD").Cell(4, 1).Value = "hdr";
                wb.SaveAs(template);
            }
            var outPath = Path.Combine(dir, "out.xlsx");
            var exporter = new OCR.Business.VbdBn.VbdBnExcelExporter(new FakeVietBdExporter());
            var rows = new List<OCR.Business.VbdBn.CccdExportRow>
            {
                new("AB 123456", new OCR.Business.VbdBn.CccdRecord
                {
                    loai_giay_to = "cccd", so_giay_to = "012345678901", ho_ten = "Nguyễn Văn A",
                    trang = new List<int> { 3, 4 }, do_tin_cay = "cao"
                }, "ho-so-GTK.pdf", Array.Empty<string>()),
                new("", new OCR.Business.VbdBn.CccdRecord { loai_giay_to = "cmnd", so_giay_to = "123456789" },
                    "khac-GTK.pdf", new[] { "Không tìm thấy GCN cùng thư mục" }),
            };

            var (gcnRows, cccdRows) = exporter.Write(
                new[] { new VietBdGcnEnvelope() }, rows, outPath, template, maXa: "12345");

            AssertEqual(1, gcnRows, "số dòng GCN từ exporter VBD (fake)");
            AssertEqual(2, cccdRows, "số dòng CCCD đã ghi");
            using var check = new ClosedXML.Excel.XLWorkbook(outPath);
            var ws = check.Worksheet("ThongTinCCCD");
            AssertEqual("AB 123456", ws.Cell(5, 1).GetString(), "A5 = serial");
            AssertEqual("CCCD", ws.Cell(5, 2).GetString(), "B5 = loại hiển thị");
            AssertEqual("012345678901", ws.Cell(5, 3).GetString(), "C5 = số");
            AssertEqual("3, 4", ws.Cell(5, 14).GetString(), "N5 = trang");
            AssertEqual("", ws.Cell(6, 1).GetString(), "A6 = serial trống");
            AssertTrue(ws.Cell(6, 16).GetString().Contains("Không tìm thấy GCN"), "P6 chứa cảnh báo bổ sung");

            // Template thiếu sheet ThongTinCCCD → ném lỗi rõ.
            var badTemplate = Path.Combine(dir, "bad.xlsx");
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            { wb.AddWorksheet("KeKhaiDangKy"); wb.SaveAs(badTemplate); }
            try
            {
                exporter.Write(Array.Empty<VietBdGcnEnvelope>(), rows, Path.Combine(dir, "out2.xlsx"), badTemplate, null);
                throw new Exception("Phải ném lỗi khi template thiếu sheet ThongTinCCCD");
            }
            catch (Exception ex) when (ex.Message.Contains("ThongTinCCCD")) { }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
