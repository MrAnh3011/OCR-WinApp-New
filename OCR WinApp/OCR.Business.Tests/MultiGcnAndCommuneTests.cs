using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using OCR.Business.Ai;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.VietBdGcn;

// Test cho hai nhóm thay đổi:
//  A. Phát hiện PDF ghép nhiều GCN sau khi bật `responseSchema`. Schema ép đầu ra là object nên tín hiệu
//     cũ (model tự đổi sang JSON array) biến mất → chuyển sang trường đếm tường minh
//     `so_luong_gcn_trong_file`. Đồng thời sửa false positive: array ĐÚNG 1 phần tử không còn bị báo lỗi.
//  B. Cột "Xã, huyện, tỉnh" của khuôn iLIS không được mất tên xã (`xa_phuong`).
internal static partial class Program
{
    private static async Task RunMultiGcnAndCommuneTests()
    {
        await NewGcnUnwrapsSingleElementArrayInsteadOfThrowing();
        await NewGcnThrowsWhenModelCountsMultipleGcnInFile();
        await VietBdUnwrapsSingleElementArrayAndThrowsOnMultipleGcn();
        await NewGcnAndVietBdSendResponseSchema();
        NewGcnAndVietBdSchemasDeclareCodeFieldsAsStrings();
        BothGcnSchemasDeclarePropertyOrderingAndRequireOwners();
        await BothOcrScreensRetryWhenOwnersAreMissing();
        BothPromptsRequireSingleObjectAndGcnCount();
        NewGcnExcelExporterKeepsCommuneNameInXaHuyenTinhColumns();
        await BothOcrScreensRetryThreeTimesWithEscalatingEffort();
    }

    /// <summary>
    /// Yêu cầu nghiệp vụ: file OCR lỗi được thử lại 3 lần và các lượt thử lại phải dùng mức reasoning CAO
    /// HƠN lượt đầu. Trước đây payload chỉ dựng MỘT lần trước vòng retry nên mọi lượt lặp y nguyên effort
    /// `medium` — gọi lại đúng request đã fail thì phần lớn fail tiếp.
    /// Kiểm ở hai tầng: (1) hai service OCR có khai báo escalate + đúng 3 lượt; (2) client HTTP thật sự
    /// đổi `thinkingLevel` trong payload của lượt thứ hai.
    /// </summary>
    private static async Task BothOcrScreensRetryThreeTimesWithEscalatingEffort()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "retry.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            // (1) Tầng service: cả hai màn phải xin escalate và dùng đúng 3 lượt.
            var ilisAi = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]));
            await NewIlisService(ilisAi, root).ProcessFileAsync(sourcePdf, logCallback: null);
            AssertEqual("medium", ilisAi.Calls[0].ReasoningEffort, "iLIS: lượt đầu giữ effort rẻ (medium)");
            AssertEqual("high", ilisAi.Calls[0].EscalatedReasoningEffort,
                "iLIS: phải khai báo mức escalate cho các lượt thử lại");
            AssertEqual(3, ilisAi.Calls[0].MaxAttempts, "iLIS: đúng 3 lượt gọi cho mỗi file");

            var vietBdAi = new FakeAiModelClient(CreateVietBdJson(gcnCount: 1, parcelNumber: "10"));
            await NewVietBdService(vietBdAi, root, "retry").ProcessFileAsync(sourcePdf, logCallback: null);
            AssertEqual("medium", vietBdAi.Calls[0].ReasoningEffort, "VietBD: lượt đầu giữ effort rẻ (medium)");
            AssertEqual("high", vietBdAi.Calls[0].EscalatedReasoningEffort,
                "VietBD: phải khai báo mức escalate cho các lượt thử lại");
            AssertEqual(3, vietBdAi.Calls[0].MaxAttempts, "VietBD: đúng 3 lượt gọi cho mỗi file");

            // (2) Tầng client thật: lượt 1 JSON hỏng -> lượt 2 phải gửi payload có mức reasoning cao hơn.
            // Kiểm CẢ HAI cách Gemini nhận cấu hình thinking, vì chúng phụ thuộc tên model:
            //   - gemini-3.x (model production hiện tại) -> "thinkingLevel": MEDIUM|HIGH
            //   - model cũ hơn                           -> "thinkingBudget": 4096|8192
            await AssertEscalatesOnRetry("gemini-3.1-flash-lite", "\"thinkingLevel\":\"MEDIUM\"", "\"thinkingLevel\":\"HIGH\"");
            await AssertEscalatesOnRetry("gemini-2.5-pro", "\"thinkingBudget\":4096", "\"thinkingBudget\":8192");

            // Không khai escalate -> giữ hành vi cũ (3 màn Tách GCN + Đất Uỷ Ban không bị đổi chi phí).
            var plainHandler = new QueuedHttpMessageHandler(
                (HttpStatusCode.OK, GeminiBody("""{"a":""")),
                (HttpStatusCode.OK, GeminiBody("""{"a":1}""")));
            await NewGoogleClient(plainHandler).GenerateJsonAsync(
                [new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" }],
                maxAttempts: 3,
                reasoningEnabled: true,
                reasoningEffort: "medium");
            AssertEqual(plainHandler.RequestBodies[0], plainHandler.RequestBodies[1],
                "Không truyền escalate thì mọi lượt phải giữ y nguyên payload như trước.");
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>
    /// Bug thực tế (BD 755246, BD 784357): file chỉ có 1 GCN nhưng model bọc object vào mảng 1 phần tử,
    /// khiến guard cũ (chặn MỌI array) ném lỗi tự mâu thuẫn "File này chứa 1 giấy chứng nhận riêng biệt
    /// gộp trong 1 PDF". Mảng 1 phần tử phải được bóc ra và xử lý bình thường.
    /// </summary>
    private static async Task NewGcnUnwrapsSingleElementArrayInsteadOfThrowing()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "BD 755246-GCN.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var wrapped = "[" + CreateNewGcnJsonWithDeclaredParcelCount(1, ["321"]) + "]";
            var service = NewIlisService(new FakeAiModelClient(wrapped), root);

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null);

            AssertTrue(envelope is not null, "Mảng 1 phần tử phải được bóc ra, không được ném lỗi.");
            AssertEqual(1, envelope!.danh_sach_dong.Count, "Số thửa đọc được từ mảng 1 phần tử");
            AssertEqual("321", envelope.danh_sach_dong[0].td_so_thua, "Số thửa sau khi bóc mảng");
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>
    /// Với `responseSchema`, model không thể trả array nữa → tín hiệu duy nhất còn lại là trường đếm.
    /// `so_luong_gcn_trong_file` &gt; 1 phải chặn file và hướng người dùng sang "Tách GCN".
    /// </summary>
    private static async Task NewGcnThrowsWhenModelCountsMultipleGcnInFile()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "merged.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var json = WithGcnCount(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]), 3);
            var service = NewIlisService(new FakeAiModelClient(json), root);

            var caught = await CaptureAsync(() => service.ProcessFileAsync(sourcePdf, logCallback: null));

            AssertTrue(caught is not null, "so_luong_gcn_trong_file > 1 phải ném lỗi.");
            AssertTrue(caught!.Message.Contains("Tách GCN", StringComparison.OrdinalIgnoreCase),
                "Lỗi phải hướng người dùng sang chức năng Tách GCN: " + caught.Message);
            AssertTrue(caught.Message.Contains('3'),
                "Lỗi phải nêu đúng số GCN model đếm được: " + caught.Message);

            // Không được ghi cache cho file lỗi — lần chạy sau phải quét lại.
            AssertFalse(File.Exists(Path.Combine(root, "newgcn-temp", "response_new", "merged.json")),
                "File bị chặn vì ghép nhiều GCN không được để lại cache JSON.");
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>Màn VietBD là bản copy riêng nên phải có CÙNG hành vi, không được lệch.</summary>
    private static async Task VietBdUnwrapsSingleElementArrayAndThrowsOnMultipleGcn()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "AB 510150-GCN.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            // 1. Mảng 1 phần tử -> bóc ra dùng bình thường.
            var single = "[" + CreateVietBdJson(gcnCount: null, parcelNumber: "406") + "]";
            var envelope = await NewVietBdService(new FakeAiModelClient(single), root, "a")
                .ProcessFileAsync(sourcePdf, logCallback: null);

            AssertTrue(envelope is not null, "VietBD: mảng 1 phần tử phải được bóc ra, không ném lỗi.");
            AssertEqual("406", envelope!.danh_sach_dong[0].td_so_thua, "VietBD số thửa sau khi bóc mảng");

            // 2. Trường đếm > 1 -> chặn file.
            var merged = CreateVietBdJson(gcnCount: 2, parcelNumber: "406");
            var caught = await CaptureAsync(() =>
                NewVietBdService(new FakeAiModelClient(merged), root, "b")
                    .ProcessFileAsync(sourcePdf, logCallback: null));

            AssertTrue(caught is not null, "VietBD: so_luong_gcn_trong_file > 1 phải ném lỗi.");
            AssertTrue(caught!.Message.Contains("Tách GCN", StringComparison.OrdinalIgnoreCase),
                "VietBD lỗi phải hướng sang Tách GCN: " + caught.Message);
            AssertTrue(caught.Message.Contains('2'),
                "VietBD lỗi phải nêu đúng số GCN đếm được: " + caught.Message);
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>
    /// LỚP 2 phải bật cho cả hai màn OCR GCN — trước đây chỉ 3 màn Tách GCN gửi schema.
    /// </summary>
    private static async Task NewGcnAndVietBdSendResponseSchema()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "schema.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var ilisAi = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]));
            await NewIlisService(ilisAi, root).ProcessFileAsync(sourcePdf, logCallback: null);
            AssertEqual(1, ilisAi.Calls.Count, "Một lượt gọi AI cho màn iLIS.");
            AssertTrue(ilisAi.Calls[0].ResponseSchema is not null,
                "Màn OCR GCN iLIS phải gửi responseSchema (LỚP 2).");

            var vietBdAi = new FakeAiModelClient(CreateVietBdJson(gcnCount: 1, parcelNumber: "10"));
            await NewVietBdService(vietBdAi, root, "schema").ProcessFileAsync(sourcePdf, logCallback: null);
            AssertEqual(1, vietBdAi.Calls.Count, "Một lượt gọi AI cho màn VietBD.");
            AssertTrue(vietBdAi.Calls[0].ResponseSchema is not null,
                "Màn OCR GCN VietBD phải gửi responseSchema (LỚP 2).");
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>
    /// Lý do chính bật schema: các trường dạng MÃ phải là STRING để model không xuất number rồi làm hỏng
    /// cú pháp JSON (số `0` đứng đầu, hoặc dấu `"` chèn nhầm giữa dãy số dài). Và trường đếm số GCN phải
    /// nằm trong `required` — nếu model bỏ qua thì mất hẳn khả năng phát hiện PDF ghép.
    /// </summary>
    private static void NewGcnAndVietBdSchemasDeclareCodeFieldsAsStrings()
    {
        AssertSchemaShape(NewGcnResponseSchema.Instance, "iLIS", ["ma_vach", "ma_ho_gia_dinh", "ma_xa"]);
        AssertSchemaShape(VietBdGcnResponseSchema.Instance, "VietBD",
            ["ma_vach", "ma_loai_gcn", "ma_xa", "ma_loai_nha_rieng_le", "ma_quyen_so_huu"]);
    }

    private static void AssertSchemaShape(object schema, string screen, string[] codeFields)
    {
        var root = (Dictionary<string, object>)schema;
        AssertEqual("OBJECT", (string)root["type"],
            $"Schema {screen} phải ràng buộc đầu ra là OBJECT (không phải ARRAY).");

        var required = (string[])root["required"];
        AssertTrue(Array.IndexOf(required, "so_luong_gcn_trong_file") >= 0,
            $"Schema {screen} phải bắt buộc model khai báo so_luong_gcn_trong_file.");

        var properties = (Dictionary<string, object>)root["properties"];
        var count = (Dictionary<string, object>)properties["so_luong_gcn_trong_file"];
        AssertEqual("INTEGER", (string)count["type"], $"Schema {screen}: so_luong_gcn_trong_file phải là INTEGER.");
        AssertFalse(count.ContainsKey("nullable"),
            $"Schema {screen}: so_luong_gcn_trong_file không được nullable, nếu không model sẽ trả null.");

        // Mọi trường dạng mã, ở bất kỳ độ sâu nào, phải là STRING.
        var json = JsonSerializer.Serialize(schema);
        foreach (var field in codeFields)
        {
            AssertTrue(json.Contains($"\"{field}\":{{\"type\":\"STRING\"", StringComparison.Ordinal),
                $"Schema {screen}: trường mã `{field}` phải khai báo STRING để JSON không bị hỏng cú pháp.");
        }
    }

    /// <summary>Prompt phải khớp schema: cấm trả mảng và yêu cầu đếm số GCN.</summary>
    private static void BothPromptsRequireSingleObjectAndGcnCount()
    {
        foreach (var (resourceSuffix, screen) in new[]
                 {
                     ("PROMPT_TRICH_XUAT_GCN.md", "iLIS"),
                     ("PROMPT_TRICH_XUAT_GCN_VIETBD.md", "VietBD")
                 })
        {
            var prompt = ReadEmbeddedPrompt(resourceSuffix);
            AssertTrue(prompt.Contains("\"so_luong_gcn_trong_file\": \"int\"", StringComparison.Ordinal),
                $"Prompt {screen} phải khai báo so_luong_gcn_trong_file trong JSON schema đầu ra.");
            AssertTrue(prompt.Contains("KHÔNG BAO GIỜ** là mảng", StringComparison.Ordinal),
                $"Prompt {screen} phải cấm model trả về mảng.");
            AssertTrue(prompt.Contains("vẫn là 1 GCN", StringComparison.Ordinal),
                $"Prompt {screen} phải nói rõ 1 GCN nhiều thửa vẫn đếm là 1.");
        }
    }

    /// <summary>
    /// Bug thực tế: cột "Xã, huyện, tỉnh" (U của chủ, AL của vợ/chồng) chỉ ghi `xa_huyen_tinh`, mà model
    /// tách tên xã ra trường riêng `xa_phuong` → tên xã không vào cột nào, Excel mất dữ liệu xã.
    /// Cột "Mã xã/phường" (T/AK) vẫn phải để trống: giấy không in mã xã, không được suy đoán.
    /// </summary>
    private static void NewGcnExcelExporterKeepsCommuneNameInXaHuyenTinhColumns()
    {
        var root = NewTestRoot();
        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = CreateNewGcnEnvelope(
                "spouse.pdf",
                "vo_chong",
                [
                    // Dữ liệu thật từ cache BD 755045: xã ở trường riêng, chuỗi kia chỉ có huyện + tỉnh.
                    NewOwnerWithCommune("Bùi Văn Pẩu", "xã Đồng Tâm", "huyện Bình Liêu - tỉnh Quảng Ninh"),
                    NewOwnerWithCommune("Vi Thị Cáu", "xã Đồng Tâm", "huyện Bình Liêu - tỉnh Quảng Ninh")
                ],
                "321");

            new NewGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");

            AssertEqual("xã Đồng Tâm - huyện Bình Liêu - tỉnh Quảng Ninh",
                data.Cell(5, "U").Value.ToString(),
                "Cột U 'Xã, huyện, tỉnh' của chủ phải giữ tên xã");
            AssertEqual("xã Đồng Tâm - huyện Bình Liêu - tỉnh Quảng Ninh",
                data.Cell(5, "AL").Value.ToString(),
                "Cột AL 'Xã, huyện, tỉnh' của vợ/chồng phải giữ tên xã");
            AssertEqual("", data.Cell(5, "T").Value.ToString(),
                "Cột T 'Mã xã/phường' vẫn để trống — giấy không in mã xã");
            AssertEqual("", data.Cell(5, "AK").Value.ToString(),
                "Cột AK 'Mã xã/Phường' vẫn để trống — giấy không in mã xã");

            // Model đôi khi đã gộp sẵn tên xã vào chuỗi -> không được nhân đôi.
            var alreadyJoined = CreateNewGcnEnvelope(
                "joined.pdf",
                "ca_nhan",
                [NewOwnerWithCommune("Vi Văn Voòng", "xã Đồng Tâm",
                    "xã Đồng Tâm, huyện Bình Liêu, tỉnh Quảng Ninh")],
                "322");
            var joinedOutput = Path.Combine(root, "joined.xlsx");
            new NewGcnExcelExporter().Write([alreadyJoined], joinedOutput, templatePath);

            using var joined = new XLWorkbook(joinedOutput);
            AssertEqual("xã Đồng Tâm, huyện Bình Liêu, tỉnh Quảng Ninh",
                joined.Worksheet("Data").Cell(5, "U").Value.ToString(),
                "Tên xã đã có sẵn trong chuỗi thì không được ghép thêm lần nữa");
        }
        finally { DeleteTestRoot(root); }
    }

    /// <summary>
    /// Lượt 1 trả JSON cắt cụt → client phải gọi lại; payload lượt 2 mang mức reasoning ĐÃ NÂNG.
    /// </summary>
    private static async Task AssertEscalatesOnRetry(string model, string expectedFirst, string expectedRetry)
    {
        var handler = new QueuedHttpMessageHandler(
            (HttpStatusCode.OK, GeminiBody("""{"a":""")),
            (HttpStatusCode.OK, GeminiBody("""{"a":1}""")));
        var client = new AiModelClient(
            new AiProviderOptions
            {
                Provider = "google-ai-studio",
                Url = "https://example.invalid",
                ApiKey = "test-key",
                Model = model
            },
            handler);

        var result = await client.GenerateJsonAsync(
            [new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" }],
            maxAttempts: 3,
            reasoningEnabled: true,
            reasoningEffort: "medium",
            escalatedReasoningEffort: "high");

        AssertEqual("""{"a":1}""", result, $"[{model}] Lượt thử lại phải trả về JSON hợp lệ.");
        AssertEqual(2, handler.RequestBodies.Count, $"[{model}] Phải có đúng một lượt thử lại.");
        AssertTrue(handler.RequestBodies[0].Contains(expectedFirst, StringComparison.Ordinal),
            $"[{model}] Payload lượt 1 phải mang mức gốc {expectedFirst}: " + handler.RequestBodies[0]);
        AssertTrue(handler.RequestBodies[1].Contains(expectedRetry, StringComparison.Ordinal),
            $"[{model}] Payload lượt 2 phải mang mức ĐÃ NÂNG {expectedRetry}: " + handler.RequestBodies[1]);
    }

    // ------------------------------------------------------------------ helpers ----
    private static NewGcnOwner NewOwnerWithCommune(string name, string commune, string districtProvince)
        => new()
        {
            ho_ten = name,
            to_dan_pho = "Thôn Nà Tào",
            xa_phuong = commune,
            xa_huyen_tinh = districtProvince,
            dia_chi_day_du = $"Thôn Nà Tào - {commune} - {districtProvince}"
        };

    private static NewGcnExtractService NewIlisService(FakeAiModelClient ai, string root)
        => new(
            new FakePdfRenderer(),
            ai,
            new NewGcnOptions { OptimizeImages = false, TempDir = Path.Combine(root, "newgcn-temp") });

    private static VietBdGcnExtractService NewVietBdService(FakeAiModelClient ai, string root, string cacheKey)
        => new(
            new FakePdfRenderer(),
            ai,
            new VietBdGcnOptions { OptimizeImages = false, TempDir = Path.Combine(root, "vietbd-temp", cacheKey) });

    /// <summary>Gắn `so_luong_gcn_trong_file` vào JSON envelope đã dựng sẵn.</summary>
    private static string WithGcnCount(string envelopeJson, int count)
    {
        var envelope = JsonSerializer.Deserialize<NewGcnEnvelope>(envelopeJson)!;
        envelope.so_luong_gcn_trong_file = count;
        return JsonSerializer.Serialize(envelope);
    }

    private static string CreateVietBdJson(int? gcnCount, string parcelNumber)
    {
        var envelope = new VietBdGcnEnvelope
        {
            so_luong_gcn_trong_file = gcnCount,
            thong_tin_gcn = new VietBdGcnInfo
            {
                so_serial = "AB 510150",
                loai_quan_he = "ca_nhan",
                ma_loai_gcn = "11",
                so_luong_thua_dat_doc_duoc = 1,
                chu_su_dung_chi_tiet = [new VietBdGcnOwner { ho_ten = "Nguyễn Xuân Quý", xa = "xã Thanh Lân" }],
                canh_bao = []
            },
            danh_sach_dong =
            [
                new VietBdGcnRow { td_so_thua = parcelNumber, td_so_to = "02", td_tong_dien_tich = "11182" }
            ]
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static string ReadEmbeddedPrompt(string resourceSuffix)
    {
        var asm = typeof(NewGcnExcelExporter).Assembly;
        string? name = null;
        foreach (var candidate in asm.GetManifestResourceNames())
            if (candidate.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                name = candidate;

        AssertTrue(name is not null, $"Không tìm thấy embedded prompt {resourceSuffix}.");
        using var stream = asm.GetManifestResourceStream(name!)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string NewTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTestRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    // ---------------------------------------------------------------------------------------------
    // C. Bug thực tế 2026-08-13 (thư mục TestCSD): GCN in rõ chủ sử dụng nhưng OCR trả
    //    `chu_su_dung_chi_tiet = null` và KHÔNG báo lỗi. Nguyên nhân: `properties` là map trong proto
    //    của Google API nên mất thứ tự khai báo; thiếu `propertyOrdering`, model sinh key xáo trộn rồi
    //    bỏ trắng gần hết trường (đo được: 197 token đầu ra so với 1239 token khi bỏ schema).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Mọi OBJECT trong hai schema GCN phải khai `propertyOrdering` đúng thứ tự `properties`, và
    /// `thong_tin_gcn` phải bắt buộc có `chu_su_dung_chi_tiet` để model không được phép bỏ qua chủ.
    /// </summary>
    private static void BothGcnSchemasDeclarePropertyOrderingAndRequireOwners()
    {
        AssertSchemaOrdering(NewGcnResponseSchema.Instance, "iLIS");
        AssertSchemaOrdering(VietBdGcnResponseSchema.Instance, "VietBD");
        VietBdSchemaRequiresSignatureFields();
    }

    /// <summary>
    /// Bug thực tế 27/08/2026 (lô VanSon_Thu): model flash-lite trả null đồng loạt cụm ký
    /// (`ky_so_vao_so`/`ky_ngay_ky_gcn`/`ky_nguoi_ky`) và `do_tin_cay` ở MỌI file, kể cả mẫu QR in rõ,
    /// không kèm cảnh báo → Excel trống 3 cột O/R/S và mất tô màu cảnh báo. required + nullable buộc
    /// model luôn khai báo các trường này (giá trị vẫn được null khi giấy thật sự không đọc được).
    /// </summary>
    private static void VietBdSchemaRequiresSignatureFields()
    {
        var root = (Dictionary<string, object>)VietBdGcnResponseSchema.Instance;
        var properties = (Dictionary<string, object>)root["properties"];

        var rows = (Dictionary<string, object>)properties["danh_sach_dong"];
        var row = (Dictionary<string, object>)rows["items"];
        var rowRequired = (string[])row["required"];
        foreach (var field in new[] { "ky_so_vao_so", "ky_ngay_ky_gcn", "ky_nguoi_ky" })
            AssertTrue(Array.IndexOf(rowRequired, field) >= 0,
                $"Schema VietBD: `{field}` phải nằm trong required của dòng thửa (cột O/R/S của Excel).");

        var info = (Dictionary<string, object>)properties["thong_tin_gcn"];
        var infoRequired = (string[])info["required"];
        AssertTrue(Array.IndexOf(infoRequired, "do_tin_cay") >= 0,
            "Schema VietBD: `do_tin_cay` phải required, nếu không exporter mất cơ chế tô màu cảnh báo.");
    }

    private static void AssertSchemaOrdering(object schema, string screen)
    {
        int objectCount = WalkSchemaObjects(schema, screen);
        AssertTrue(objectCount >= 4,
            $"Schema {screen}: phải duyệt được các object lồng nhau (gốc/thong_tin_gcn/chủ/dòng thửa).");

        var info = (Dictionary<string, object>)
            ((Dictionary<string, object>)((Dictionary<string, object>)schema)["properties"])["thong_tin_gcn"];
        var required = (string[])info["required"];
        AssertTrue(Array.IndexOf(required, "chu_su_dung_chi_tiet") >= 0,
            $"Schema {screen}: `chu_su_dung_chi_tiet` phải nằm trong required của thong_tin_gcn.");
        AssertTrue(Array.IndexOf(required, "so_luong_thua_dat_doc_duoc") >= 0,
            $"Schema {screen}: `so_luong_thua_dat_doc_duoc` phải required, nếu không hậu kiểm lệch số thửa vô hiệu.");
    }

    /// <summary>Duyệt đệ quy schema, trả về số OBJECT đã kiểm tra.</summary>
    private static int WalkSchemaObjects(object node, string screen)
    {
        if (node is not Dictionary<string, object> map) return 0;

        int count = 0;
        if (map.TryGetValue("type", out var type) && (string)type == "OBJECT")
        {
            count++;
            var properties = (Dictionary<string, object>)map["properties"];
            AssertTrue(map.ContainsKey("propertyOrdering"),
                $"Schema {screen}: mọi OBJECT phải khai propertyOrdering (properties là map, mất thứ tự khi gửi).");
            AssertEqual(
                string.Join(",", properties.Keys),
                string.Join(",", (string[])map["propertyOrdering"]),
                $"Schema {screen}: propertyOrdering phải khớp đúng thứ tự properties.");

            foreach (var child in properties.Values) count += WalkSchemaObjects(child, screen);
        }

        if (map.TryGetValue("items", out var items)) count += WalkSchemaObjects(items, screen);
        return count;
    }

    /// <summary>
    /// Envelope không có chủ sử dụng nào = dấu hiệu model đọc sót (mục 1 của GCN luôn được in).
    /// Phải đọc lại đúng một lượt ở mức reasoning cao nhất, và nếu vẫn trống thì gắn `canh_bao`
    /// thay vì lặng lẽ xuất Excel trống chủ.
    /// </summary>
    private static async Task BothOcrScreensRetryWhenOwnersAreMissing()
    {
        var root = NewTestRoot();
        try
        {
            var sourcePdf = Path.Combine(root, "thieu-chu.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            // iLIS: lượt 1 thiếu chủ → lượt 2 có chủ.
            var ilisAi = new FakeAiModelClient(
                WithoutOwners(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"])),
                CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]));
            var ilis = await NewIlisService(ilisAi, root).ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(2, ilisAi.Calls.Count, "iLIS phải đọc lại đúng một lượt khi thiếu chủ sử dụng.");
            AssertEqual("high", ilisAi.Calls[1].ReasoningEffort,
                "Lượt đọc lại chủ sử dụng phải dùng mức reasoning cao nhất.");
            AssertEqual(1, ilis?.thong_tin_gcn.chu_su_dung_chi_tiet?.Count,
                "iLIS phải lấy được chủ sử dụng từ lượt đọc lại.");

            // iLIS: cả hai lượt đều thiếu chủ → giữ envelope đầu và gắn cảnh báo.
            var ilisEmptyAi = new FakeAiModelClient(
                WithoutOwners(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"])),
                WithoutOwners(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"])));
            var ilisEmpty = await NewIlisService(ilisEmptyAi, Path.Combine(root, "ilis-empty"))
                .ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(2, ilisEmptyAi.Calls.Count, "iLIS chỉ đọc lại MỘT lượt, không lặp vô hạn.");
            AssertTrue(
                ilisEmpty?.thong_tin_gcn.canh_bao?.Exists(
                    w => w.Contains("chu_su_dung_chi_tiet", StringComparison.Ordinal)) == true,
                "Thiếu chủ sử dụng sau hậu kiểm phải để lại cảnh báo, không được im lặng.");

            // VietBD: cùng hành vi.
            var vietBdAi = new FakeAiModelClient(
                WithoutVietBdOwners(CreateVietBdJson(gcnCount: 1, parcelNumber: "10")),
                CreateVietBdJson(gcnCount: 1, parcelNumber: "10"));
            var vietBd = await NewVietBdService(vietBdAi, root, "thieu-chu")
                .ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(2, vietBdAi.Calls.Count, "VietBD phải đọc lại đúng một lượt khi thiếu chủ sử dụng.");
            AssertEqual("high", vietBdAi.Calls[1].ReasoningEffort,
                "Lượt đọc lại chủ sử dụng của VietBD phải dùng mức reasoning cao nhất.");
            AssertEqual(1, vietBd?.thong_tin_gcn.chu_su_dung_chi_tiet?.Count,
                "VietBD phải lấy được chủ sử dụng từ lượt đọc lại.");
        }
        finally { DeleteTestRoot(root); }
    }

    private static string WithoutOwners(string envelopeJson)
    {
        var envelope = JsonSerializer.Deserialize<NewGcnEnvelope>(envelopeJson)!;
        envelope.thong_tin_gcn.chu_su_dung_chi_tiet = null;
        return JsonSerializer.Serialize(envelope);
    }

    private static string WithoutVietBdOwners(string envelopeJson)
    {
        var envelope = JsonSerializer.Deserialize<VietBdGcnEnvelope>(envelopeJson)!;
        envelope.thong_tin_gcn.chu_su_dung_chi_tiet = null;
        return JsonSerializer.Serialize(envelope);
    }
}
