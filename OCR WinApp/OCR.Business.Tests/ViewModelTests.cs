using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.Split;
using OCR.Business.UyBan;
using OCR_WinApp.Services;
using OCR_WinApp.ViewModels;

// Test full-flow gọi thẳng StartAsync/PauseCommand/ExportCommand của 3 ViewModel (GcnNewViewModel,
// TachGcnViewModel, DatUyBanViewModel) qua ctor thật + fake cho mọi dependency ngoài, không qua UI.
// Xem CLAUDE.md/task gốc: DispatcherQueue.GetForCurrentThread() trả null trong console app này nên
// Ui(...) chạy đồng bộ ngay tại chỗ — nhờ vậy test tất định, không cần Thread.Sleep.
internal static partial class Program
{
    private static async Task RunViewModelTests()
    {
        // A. Bấm Dừng — không dòng nào kẹt, tiến độ vẫn đủ (cả 3 màn).
        await GcnNewPauseSettlesEveryRowWithoutStuckStatus();
        await TachGcnPauseSettlesEveryRowWithoutStuckStatus();
        await DatUyBanPauseSettlesEveryRowWithoutStuckStatus();

        // B. Lô tách ra 0 file -> Export KHÔNG được bật (màn Tách).
        await TachGcnZeroFilesCreatedKeepsExportDisabled();

        // C. Bấm Dừng -> quota KHÔNG bị trừ; chạy xong -> ghi đúng 1 lần theo tổng FilesCreated (màn Tách).
        await TachGcnPauseSkipsQuotaButCompletedRunRecordsExactlyOnce();

        // D. File hỏng upload vĩnh viễn vẫn được đếm, tiến độ vẫn đạt 100%, Export vẫn bật nếu còn thành công.
        await GcnNewCountsPermanentUploadFailuresAndKeepsExportEnabledWithPartialSuccess();
        await DatUyBanCountsPermanentUploadFailuresAndKeepsExportEnabledWithPartialSuccess();

        // E. Lỗi inference không bị ghi đè bằng câu lỗi chung của vòng quét Failures (hồi quy Task 5).
        await GcnNewInferenceErrorMessageIsNotOverwrittenByGenericFailure();
        await TachGcnInferenceErrorMessageIsNotOverwrittenByGenericFailure();

        // F. Lô toàn thành công -> không file nào lọt vào danh sách file lỗi.
        await GcnNewFullSuccessRunKeepsFailedSourcesExportDisabled();
        await TachGcnFullSuccessRunKeepsFailedSourcesExportDisabled();
    }

    // ---------------------------------------------------------------------
    // Helpers dựng dữ liệu + ViewModel
    // ---------------------------------------------------------------------

    private static string CreateTempRoot(string label)
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", "viewmodel", label, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static List<string> CreateDummyPdfFiles(string folder, int count)
    {
        Directory.CreateDirectory(folder);
        var paths = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(folder, $"file-{i:D2}.pdf");
            // Nội dung giả — đủ để File.ReadAllBytesAsync đọc được, không cần PDF hợp lệ vì
            // các fake không parse nội dung.
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            paths.Add(path);
        }
        return paths;
    }

    private static GcnNewViewModel CreateGcnNewViewModel(
        IFolderPickerService folderPicker,
        INewGcnExtractService extract,
        string cacheRoot,
        IAuthService? auth = null,
        IGeminiFileApiService? geminiFiles = null,
        INewGcnExcelExporter? excel = null,
        int workers = 1)
    {
        var files = geminiFiles ?? new FakeGeminiFileApiService();
        var uploadOptions = new GeminiUploadOptions { Workers = 3, MaxRetries = 2, RetryBaseDelayMs = 0 };
        return new GcnNewViewModel(
            folderPicker,
            extract,
            excel ?? new FakeNewGcnExcelExporter(),
            new FakeExportResultNotifier(),
            new FakeErrorLogService(),
            files,
            new GeminiUploadPipeline(files, uploadOptions),
            auth ?? new FakeAuthService(),
            new NewGcnRunCacheService(cacheRoot),
            new FakeSplitCachePromptService(),
            new NewGcnOptions { Workers = workers },
            new FakePdfRenderer());
    }

    private static TachGcnViewModel CreateTachGcnViewModel(
        IFolderPickerService folderPicker,
        ISplitGcnService split,
        string cacheRoot,
        IAuthService? auth = null,
        IGeminiFileApiService? geminiFiles = null,
        int workers = 1)
    {
        var files = geminiFiles ?? new FakeGeminiFileApiService();
        var uploadOptions = new GeminiUploadOptions { Workers = 3, MaxRetries = 2, RetryBaseDelayMs = 0 };
        var opt = new SplitGcnOptions { Workers = workers, TempDir = cacheRoot };
        return new TachGcnViewModel(
            folderPicker,
            split,
            new SplitRunCacheService(opt),
            new FakeSplitCachePromptService(),
            new FakeExportResultNotifier(),
            new FakeErrorLogService(),
            files,
            new GeminiUploadPipeline(files, uploadOptions),
            auth ?? new FakeAuthService(),
            opt);
    }

    private static DatUyBanViewModel CreateDatUyBanViewModel(
        IFolderPickerService folderPicker,
        IUyBanSplitService split,
        IUyBanExtractService extract,
        string tempDir,
        IAuthService? auth = null,
        IGeminiFileApiService? geminiFiles = null,
        IUyBanExcelExporter? excel = null,
        int workers = 1)
    {
        var files = geminiFiles ?? new FakeGeminiFileApiService();
        var uploadOptions = new GeminiUploadOptions { Workers = 3, MaxRetries = 2, RetryBaseDelayMs = 0 };
        return new DatUyBanViewModel(
            folderPicker,
            split,
            extract,
            excel ?? new FakeUyBanExcelExporter(),
            new FakeExportResultNotifier(),
            new FakeErrorLogService(),
            files,
            new GeminiUploadPipeline(files, uploadOptions),
            auth ?? new FakeAuthService(),
            new UyBanOptions { Workers = workers, TempDir = tempDir },
            new FakePdfRenderer());
    }

    // ---------------------------------------------------------------------
    // A. Bấm Dừng — không dòng nào kẹt, tiến độ vẫn đủ.
    // ---------------------------------------------------------------------

    private static async Task GcnNewPauseSettlesEveryRowWithoutStuckStatus()
    {
        var root = CreateTempRoot("gcn-new-pause");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            var sourceFiles = CreateDummyPdfFiles(sourceFolder, 4);

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var extract = new FakeNewGcnExtractService(async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct); // ném OperationCanceledException khi bấm Dừng.
                return null;
            });

            var vm = CreateGcnNewViewModel(new FakeFolderPickerService(sourceFolder), extract, cacheRoot, workers: 1);

            await vm.PickFolderCommand.ExecuteAsync(null);
            AssertEqual(sourceFiles.Count, vm.SelectedFiles.Count, "GcnNew phải quét đủ file nguồn trước khi Start.");

            var runTask = vm.StartCommand.ExecuteAsync(null);
            await started.Task; // đợi ít nhất 1 file thật sự bắt đầu xử lý.
            vm.PauseCommand.Execute(null);
            await runTask;

            AssertFalse(vm.IsRunning, "GcnNew: IsRunning phải về false sau khi Dừng.");
            AssertEqual(100d, vm.ProgressValue, "GcnNew: tiến độ vẫn phải đạt 100% dù bị Dừng giữa chừng.");
            AssertEqual(sourceFiles.Count, vm.Results.Count, "GcnNew: không dòng nào được thêm/mất khi Dừng giữa chừng.");
            foreach (var row in vm.Results)
            {
                AssertTrue(
                    row.TrangThai != "Chờ xử lý" && !row.TrangThai.StartsWith("⏳", StringComparison.Ordinal),
                    $"GcnNew: dòng {row.FileName} bị kẹt ở trạng thái '{row.TrangThai}' sau khi Dừng.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TachGcnPauseSettlesEveryRowWithoutStuckStatus()
    {
        var root = CreateTempRoot("tach-gcn-pause");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            var sourceFiles = CreateDummyPdfFiles(sourceFolder, 4);

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var split = new FakeSplitGcnService(async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new SplitGcnResult();
            });

            var vm = CreateTachGcnViewModel(new FakeFolderPickerService(sourceFolder), split, cacheRoot, workers: 1);

            await vm.PickFolderCommand.ExecuteAsync(null);
            AssertEqual(sourceFiles.Count, vm.SelectedFiles.Count, "TachGcn phải quét đủ file PDF nguồn trước khi Start.");

            var runTask = vm.StartCommand.ExecuteAsync(null);
            await started.Task;
            vm.PauseCommand.Execute(null);
            await runTask;

            AssertFalse(vm.IsRunning, "TachGcn: IsRunning phải về false sau khi Dừng.");
            AssertEqual(100d, vm.ProgressValue, "TachGcn: tiến độ vẫn phải đạt 100% dù bị Dừng giữa chừng.");
            AssertEqual(sourceFiles.Count, vm.Results.Count, "TachGcn: không dòng nào được thêm/mất khi Dừng giữa chừng.");
            foreach (var row in vm.Results)
            {
                AssertTrue(
                    row.TrangThai != "Chờ xử lý" && !row.TrangThai.StartsWith("⏳", StringComparison.Ordinal),
                    $"TachGcn: dòng {row.FileName} bị kẹt ở trạng thái '{row.TrangThai}' sau khi Dừng.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task DatUyBanPauseSettlesEveryRowWithoutStuckStatus()
    {
        var root = CreateTempRoot("dat-uy-ban-pause");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var tempDir = Path.Combine(root, "temp");
            var sourceFiles = CreateDummyPdfFiles(sourceFolder, 4);

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var extract = new FakeUyBanExtractService(async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new UyBanRecord();
            });

            var vm = CreateDatUyBanViewModel(
                new FakeFolderPickerService(sourceFolder), new FakeUyBanSplitService(), extract, tempDir, workers: 1);

            await vm.PickFolderCommand.ExecuteAsync(null);
            AssertEqual(sourceFiles.Count, vm.SelectedFiles.Count, "DatUyBan phải quét đủ file PDF nguồn trước khi Start.");

            var runTask = vm.StartCommand.ExecuteAsync(null);
            await started.Task;
            vm.PauseCommand.Execute(null);
            await runTask;

            AssertFalse(vm.IsRunning, "DatUyBan: IsRunning phải về false sau khi Dừng.");
            AssertEqual(100d, vm.ProgressValue, "DatUyBan: tiến độ vẫn phải đạt 100% dù bị Dừng giữa chừng.");
            // FakeUyBanSplitService tách 1-1 (mỗi nguồn đúng 1 con) nên số dòng lưới == số file nguồn.
            AssertEqual(sourceFiles.Count, vm.Results.Count, "DatUyBan: không dòng nào được thêm/mất khi Dừng giữa chừng.");
            foreach (var row in vm.Results)
            {
                AssertTrue(
                    row.TrangThai != "Chờ xử lý" && !row.TrangThai.StartsWith("⏳", StringComparison.Ordinal),
                    $"DatUyBan: dòng {row.FileName} bị kẹt ở trạng thái '{row.TrangThai}' sau khi Dừng.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // B. Lô tách ra 0 file -> Export KHÔNG được bật (màn Tách).
    // ---------------------------------------------------------------------

    private static async Task TachGcnZeroFilesCreatedKeepsExportDisabled()
    {
        var root = CreateTempRoot("tach-gcn-zero-output");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            CreateDummyPdfFiles(sourceFolder, 2);

            // SplitAsync không ném lỗi cho bất kỳ file nào, nhưng không tạo ra output nào (model
            // không nhận diện được tài liệu) — đây chính là ca hồi quy đã sửa: Export từng bị mở khoá
            // nhầm cho lô không có gì để xuất.
            var split = new FakeSplitGcnService((_, _) =>
                Task.FromResult(new SplitGcnResult { GcnCount = 0, FilesCreated = 0, CompleteSetCount = 0 }));

            var vm = CreateTachGcnViewModel(new FakeFolderPickerService(sourceFolder), split, cacheRoot, workers: 2);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "TachGcn: luồng phải chạy xong.");
            AssertFalse(vm.ExportCommand.CanExecute(null),
                "TachGcn: Export không được bật khi lô tách ra 0 file — bấm vào sẽ xoá workspace và " +
                "clear thư mục nguồn cho một lô không có gì để xuất.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // C. Bấm Dừng -> quota KHÔNG bị trừ; chạy xong -> ghi đúng 1 lần theo tổng FilesCreated.
    // ---------------------------------------------------------------------

    private static async Task TachGcnPauseSkipsQuotaButCompletedRunRecordsExactlyOnce()
    {
        var root = CreateTempRoot("tach-gcn-quota");
        try
        {
            var auth = new FakeAuthService();

            // Phần 1: Dừng giữa chừng -> quota KHÔNG được ghi.
            var canceledFolder = Path.Combine(root, "canceled-source");
            var canceledCache = Path.Combine(root, "canceled-cache");
            CreateDummyPdfFiles(canceledFolder, 3);

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockingSplit = new FakeSplitGcnService(async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new SplitGcnResult();
            });
            var canceledVm = CreateTachGcnViewModel(
                new FakeFolderPickerService(canceledFolder), blockingSplit, canceledCache, auth: auth, workers: 1);

            await canceledVm.PickFolderCommand.ExecuteAsync(null);
            var canceledRun = canceledVm.StartCommand.ExecuteAsync(null);
            await started.Task;
            canceledVm.PauseCommand.Execute(null);
            await canceledRun;

            AssertEqual(0, auth.RecordOcrCreditCalls.Count, "TachGcn: bấm Dừng giữa chừng không được trừ quota.");

            // Phần 2: chạy hết một lượt thành công (không Dừng) -> quota ghi đúng 1 lần theo tổng FilesCreated.
            var successFolder = Path.Combine(root, "success-source");
            var successCache = Path.Combine(root, "success-cache");
            var successFiles = CreateDummyPdfFiles(successFolder, 3);
            var createdPerFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [successFiles[0]] = 2,
                [successFiles[1]] = 3,
                [successFiles[2]] = 1
            };
            var successSplit = new FakeSplitGcnService((path, _) =>
                Task.FromResult(new SplitGcnResult
                {
                    GcnCount = createdPerFile[path],
                    FilesCreated = createdPerFile[path],
                    CompleteSetCount = 1
                }));
            var successVm = CreateTachGcnViewModel(
                new FakeFolderPickerService(successFolder), successSplit, successCache, auth: auth, workers: 2);

            await successVm.PickFolderCommand.ExecuteAsync(null);
            await successVm.StartCommand.ExecuteAsync(null);

            AssertEqual(1, auth.RecordOcrCreditCalls.Count, "TachGcn: lượt chạy hoàn tất (không Dừng) phải ghi quota đúng 1 lần.");
            AssertEqual(6, auth.RecordOcrCreditCalls[0].SoTrang, "TachGcn: soTrang phải bằng tổng FilesCreated của lô (2+3+1).");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // D. File hỏng upload vĩnh viễn vẫn được đếm, tiến độ vẫn đạt 100%, Export vẫn bật nếu còn thành công.
    // ---------------------------------------------------------------------

    private static async Task GcnNewCountsPermanentUploadFailuresAndKeepsExportEnabledWithPartialSuccess()
    {
        var root = CreateTempRoot("gcn-new-upload-fail");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            var sourceFiles = CreateDummyPdfFiles(sourceFolder, 3);
            var brokenFile = sourceFiles[0];

            // remainingFailures lớn hơn MaxRetries (2) -> luôn hỏng ở mọi lượt thử -> hỏng vĩnh viễn.
            var geminiFiles = new FakeGeminiFileApiService(remainingFailures: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [brokenFile] = 99
            });

            var extract = new FakeNewGcnExtractService((path, _) =>
                Task.FromResult<NewGcnEnvelope?>(new NewGcnEnvelope
                {
                    thong_tin_gcn = new NewGcnInfo { so_serial = "SERIAL-" + Path.GetFileNameWithoutExtension(path) },
                    danh_sach_dong = new List<NewGcnRow> { new() { td_so_thua = "12" } }
                }));

            var vm = CreateGcnNewViewModel(
                new FakeFolderPickerService(sourceFolder), extract, cacheRoot, geminiFiles: geminiFiles, workers: 2);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "GcnNew: luồng phải chạy xong.");
            AssertEqual(100d, vm.ProgressValue, "GcnNew: file hỏng upload vĩnh viễn vẫn phải được đếm vào tiến độ.");
            AssertTrue(vm.ExportCommand.CanExecute(null),
                "GcnNew: còn ít nhất 1 file thành công thì Export vẫn phải bật dù có file lỗi.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task DatUyBanCountsPermanentUploadFailuresAndKeepsExportEnabledWithPartialSuccess()
    {
        var root = CreateTempRoot("dat-uy-ban-upload-fail");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var tempDir = Path.Combine(root, "temp");
            var sourceFiles = CreateDummyPdfFiles(sourceFolder, 3);
            // FakeUyBanSplitService (childCount mặc định = 1) đặt tên PDF con tất định "{stem}.pdf"
            // ngay trong _opt.TempDir -> tính trước được đường dẫn con để cấu hình hỏng vĩnh viễn.
            var brokenChild = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(sourceFiles[0]) + ".pdf");

            var geminiFiles = new FakeGeminiFileApiService(remainingFailures: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [brokenChild] = 99
            });

            var extract = new FakeUyBanExtractService((_, _) =>
                Task.FromResult(new UyBanRecord { ThuaDatSo = "12", ToBanDoSo = "34" }));

            var vm = CreateDatUyBanViewModel(
                new FakeFolderPickerService(sourceFolder), new FakeUyBanSplitService(), extract, tempDir,
                geminiFiles: geminiFiles, workers: 2);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "DatUyBan: luồng phải chạy xong.");
            AssertEqual(100d, vm.ProgressValue, "DatUyBan: file hỏng upload vĩnh viễn vẫn phải được đếm vào tiến độ.");
            AssertTrue(vm.ExportCommand.CanExecute(null),
                "DatUyBan: còn ít nhất 1 đơn hoàn thành thì Export vẫn phải bật dù có đơn lỗi.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // E. Lỗi inference không bị ghi đè bằng câu lỗi chung (hồi quy Task 5: vòng quét queue.Failures
    //    từng chứa cả lỗi consumer, ghi đè thông báo lỗi cụ thể bằng câu lỗi chung).
    // ---------------------------------------------------------------------

    private static async Task GcnNewInferenceErrorMessageIsNotOverwrittenByGenericFailure()
    {
        var root = CreateTempRoot("gcn-new-inference-error");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            CreateDummyPdfFiles(sourceFolder, 1);
            const string specificMessage = "loi rieng cua file nay khong doc duoc du lieu";

            var extract = new FakeNewGcnExtractService((_, _) =>
                throw new InvalidOperationException(specificMessage));

            var vm = CreateGcnNewViewModel(new FakeFolderPickerService(sourceFolder), extract, cacheRoot, workers: 1);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "GcnNew: luồng phải chạy xong.");
            AssertEqual(1, vm.Results.Count, "GcnNew: file lỗi vẫn phải còn nguyên 1 dòng trên lưới.");
            AssertEqual("❌ " + specificMessage, vm.Results[0].TrangThai,
                "GcnNew: lỗi inference cụ thể không được ghi đè bằng câu lỗi chung của vòng quét queue.Failures.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TachGcnInferenceErrorMessageIsNotOverwrittenByGenericFailure()
    {
        var root = CreateTempRoot("tach-gcn-inference-error");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            CreateDummyPdfFiles(sourceFolder, 1);
            const string specificMessage = "loi rieng khi tach file nay";

            var split = new FakeSplitGcnService((_, _) =>
                throw new InvalidOperationException(specificMessage));

            var vm = CreateTachGcnViewModel(new FakeFolderPickerService(sourceFolder), split, cacheRoot, workers: 1);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "TachGcn: luồng phải chạy xong.");
            AssertEqual(1, vm.Results.Count, "TachGcn: file lỗi vẫn phải còn nguyên 1 dòng trên lưới.");
            AssertEqual("❌ " + specificMessage, vm.Results[0].TrangThai,
                "TachGcn: lỗi inference cụ thể không được ghi đè bằng câu lỗi chung của vòng quét queue.Failures.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // F. Lô toàn thành công -> không file nào bị nhét vào danh sách file lỗi (hồi quy Critical: vòng
    //    quét dòng treo từng coi mọi file thành công là lỗi).
    // ---------------------------------------------------------------------

    private static async Task GcnNewFullSuccessRunKeepsFailedSourcesExportDisabled()
    {
        var root = CreateTempRoot("gcn-new-all-success");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            CreateDummyPdfFiles(sourceFolder, 3);

            var extract = new FakeNewGcnExtractService((path, _) =>
                Task.FromResult<NewGcnEnvelope?>(new NewGcnEnvelope
                {
                    thong_tin_gcn = new NewGcnInfo { so_serial = "SERIAL-" + Path.GetFileNameWithoutExtension(path) },
                    danh_sach_dong = new List<NewGcnRow> { new() { td_so_thua = "12" } }
                }));

            var vm = CreateGcnNewViewModel(new FakeFolderPickerService(sourceFolder), extract, cacheRoot, workers: 2);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "GcnNew: luồng phải chạy xong.");
            AssertTrue(vm.ExportCommand.CanExecute(null), "GcnNew: lô toàn thành công phải bật được Export.");
            AssertFalse(vm.ExportFailedSourcesCommand.CanExecute(null),
                "GcnNew: lô toàn thành công không được có file nào lọt vào danh sách file lỗi.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TachGcnFullSuccessRunKeepsFailedSourcesExportDisabled()
    {
        var root = CreateTempRoot("tach-gcn-all-success");
        try
        {
            var sourceFolder = Path.Combine(root, "source");
            var cacheRoot = Path.Combine(root, "cache");
            CreateDummyPdfFiles(sourceFolder, 3);

            var split = new FakeSplitGcnService((_, _) =>
                Task.FromResult(new SplitGcnResult { GcnCount = 1, FilesCreated = 1, CompleteSetCount = 1 }));

            var vm = CreateTachGcnViewModel(new FakeFolderPickerService(sourceFolder), split, cacheRoot, workers: 2);

            await vm.PickFolderCommand.ExecuteAsync(null);
            await vm.StartCommand.ExecuteAsync(null);

            AssertFalse(vm.IsRunning, "TachGcn: luồng phải chạy xong.");
            AssertTrue(vm.ExportCommand.CanExecute(null), "TachGcn: lô toàn thành công phải bật được Export.");
            AssertFalse(vm.ExportFailedSourcesCommand.CanExecute(null),
                "TachGcn: lô toàn thành công không được có file nào lọt vào danh sách file lỗi.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
