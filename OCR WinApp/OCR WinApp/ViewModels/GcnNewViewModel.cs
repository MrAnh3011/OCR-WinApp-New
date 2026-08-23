using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.Notifications;
using OCR.Business.Pdf;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "OCR GCN iLIS": chọn thư mục PDF/ảnh → gọi LLM trích xuất envelope → lưới (mỗi dòng = 1 thửa) →
/// Export ra Excel form đầy đủ (port Tab 4 GcnOcrApp). Điều phối song song + real-time.
/// Tag/screen key/cache vẫn là "gcn-new" (chỉ đổi tên hiển thị) để không làm mồ côi cache và log đã có trên máy người dùng.
/// </summary>
public partial class GcnNewViewModel : ObservableObject
{
    private static readonly string[] SupportedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    private readonly IFolderPickerService _folderPicker;
    private readonly INewGcnExtractService _extract;
    private readonly INewGcnExcelExporter _excel;
    private readonly IExportResultNotifier _notifier;
    private readonly IErrorLogService _errorLog;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly IGeminiUploadPipeline _uploadPipeline;
    private readonly IAuthService _auth;
    private readonly NewGcnRunCacheService _cache;
    private readonly ISplitCachePromptService _cachePrompt;
    private readonly NewGcnOptions _opt;
    private readonly IPdfRenderer _pdf;
    private readonly DispatcherQueue? _dispatcher;

    private CancellationTokenSource? _cts;
    private NewGcnRunWorkspace? _workspace;
    private readonly List<NewGcnEnvelope> _envelopes = new();
    private readonly object _envelopesLock = new();
    private readonly Dictionary<string, NewGcnEnvelope> _successfulSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedSourcePathsLock = new();
    private DateTime _lastRunStartedAt = DateTime.Now;
    private DateTime _lastRunCompletedAt = DateTime.Now;

    /// <summary>internal (không private) để test harness gọi được <see cref="BuildRenamePlan"/>.</summary>
    internal sealed record SuccessfulGcnSource(string SourcePath, NewGcnEnvelope Envelope);
    private sealed record RenameSourceResult(int Renamed, int AlreadyNamed, int Skipped);

    public string Title => "OCR GCN iLIS";
    public string Description => "Chọn thư mục chứa PDF/ảnh GCN → trích xuất dữ liệu, mỗi dòng là một thửa. Sau khi hoàn tất, bấm Export để xuất Excel.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportFailedSourcesCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double _progressValue;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _selectedPath;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string ProgressText => $"{ProgressValue:0}%";
    public string FileCountText => $"Tìm thấy {SelectedFiles.Count} tệp (PDF/ảnh)";

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<NewGcnRecord> Results { get; } = new();

    public GcnNewViewModel(
        IFolderPickerService folderPicker,
        INewGcnExtractService extract,
        INewGcnExcelExporter excel,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        NewGcnRunCacheService cache,
        ISplitCachePromptService cachePrompt,
        NewGcnOptions opt,
        IPdfRenderer pdf)
    {
        _folderPicker = folderPicker;
        _extract = extract;
        _excel = excel;
        _notifier = notifier;
        _errorLog = errorLog;
        _geminiFiles = geminiFiles;
        _uploadPipeline = uploadPipeline;
        _auth = auth;
        _cache = cache;
        _cachePrompt = cachePrompt;
        _opt = opt;
        _pdf = pdf;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        SelectedFiles.CollectionChanged += (_, _) =>
        {
            StartCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FileCountText));
        };
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (path is null) return;
        try
        {
            var workspace = _cache.GetWorkspace("gcn-new", path);
            ResetSelection();
            AddFolder(path);
            SelectedPath = path;
            _workspace = workspace;
        }
        catch (Exception ex)
        {
            _errorLog.LogException("gcn-new", "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
            // Name = đường dẫn tương đối từ thư mục đã chọn (quét đệ quy cả thư mục con).
            SelectedFiles.Add(new SelectedFile(Path.GetRelativePath(folder, file), file));
        }
    }

    private void ResetSelection()
    {
        SelectedFiles.Clear();
        Results.Clear();
        lock (_envelopesLock)
        {
            _envelopes.Clear();
            _successfulSources.Clear();
        }
        ClearFailedSourcePaths();
        ProgressValue = 0;
        StatusMessage = null;
        ExportCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsRunning && SelectedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedFiles.Count == 0 || _workspace is null) return;

        var useCachedJson = false;
        try
        {
            await _geminiFiles.PrepareExecutionAsync();
            if (_cache.HasJsonCache(_workspace, SelectedFiles.Select(f => f.Path)))
            {
                var choice = await _cachePrompt.AskAsync();
                if (choice == SplitCacheChoice.Cancel) return;
                useCachedJson = choice == SplitCacheChoice.UseExisting;
                if (choice == SplitCacheChoice.Rescan)
                    await _cache.ResetWorkspaceAsync(_workspace);
            }
            else if (_cache.HasJsonCache(_workspace))
            {
                await _cache.ResetWorkspaceAsync(_workspace);
            }
        }
        catch (Exception ex)
        {
            _errorLog.LogException("gcn-new", "StartAsync.PrepareCache", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
            return;
        }

        ErrorMessage = null;
        Results.Clear();
        lock (_envelopesLock)
        {
            _envelopes.Clear();
            _successfulSources.Clear();
        }
        ClearFailedSourcePaths();
        IsRunning = true;
        ProgressValue = 0;
        _lastRunStartedAt = DateTime.Now;
        _lastRunCompletedAt = _lastRunStartedAt;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var selected = SelectedFiles.ToList();
        var files = selected.Select(f => f.Path).ToList();
        var workspace = _workspace;
        var stubMap = new Dictionary<string, NewGcnRecord>();
        foreach (var sf in selected)
        {
            var stub = new NewGcnRecord { FilePath = sf.Path, FileName = sf.Name, TrangThai = "Chờ xử lý" };
            Results.Add(stub);
            stubMap[sf.Path] = stub;
        }

        int workers = Math.Max(1, _opt.Workers);
        int done = 0, success = 0, failed = 0;
        var startTime = DateTime.Now;

        // Chốt sổ tại ĐÚNG MỘT chỗ: mọi thay đổi tiến độ đi qua đây.
        // Truyền qua OnItemSettled trong request — KHÔNG tự "+=" vào queue.ItemSettled sau khi
        // Start trả về: producer chạy NGAY bên trong Start, nguồn nào chốt trước khi kịp "+=" sẽ
        // làm mất sự kiện, "done" không bao giờ đủ tổng số file và ProgressValue kẹt dưới 100.
        var queue = _uploadPipeline.Start(new GeminiUploadRequest
        {
            SourcePaths = files,
            WillHitJsonCache = path => useCachedJson && _cache.HasFreshJson(workspace, path),
            PrepareArtifactsAsync = (path, token) => BuildArtifactsAsync(path, token),
            OnItemUploading = sourcePath => Ui(() =>
            {
                if (stubMap.TryGetValue(sourcePath, out var stub))
                    stub.TrangThai = "⏳ Đang tải lên...";
            }),
            OnItemSettled = (sourcePath, ok) =>
            {
                if (ok) Interlocked.Increment(ref success);
                else Interlocked.Increment(ref failed);
                int d = Interlocked.Increment(ref done);

                Ui(() =>
                {
                    ProgressValue = (double)d / files.Count * 100;
                    StatusMessage = $"Đang xử lý: {d}/{files.Count}  |  ✅ {success}  ❌ {failed}{Eta(startTime, d, files.Count)}";
                });
            }
        }, ct);

        var consumers = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var item = await queue.ClaimNextAsync(ct);
                    if (item is null) break;

                    var stub = stubMap[item.SourcePath];
                    var ok = false;
                    try
                    {
                        Ui(() => stub.TrangThai = "⏳ Đang xử lý...");
                        var envelope = await _extract.ProcessFileAsync(
                            item.SourcePath, null, ct,
                            _cache.GetJsonPath(workspace, item.SourcePath), useCachedJson, item.Files);
                        if (envelope == null) throw new Exception("Không nhận được dữ liệu phù hợp từ file.");

                        lock (_envelopesLock)
                        {
                            _envelopes.Add(envelope);
                            _successfulSources[item.SourcePath] = envelope;
                        }
                        Ui(() =>
                        {
                            // Cập nhật lại stub trước khi gỡ khỏi lưới: object stub gốc vẫn có thể được
                            // đọc sau đó (ví dụ nếu có code khác duyệt stubMap), không để nó tiếp tục
                            // mang chuỗi "⏳ Đang xử lý..." dù thực chất đã xử lý xong.
                            stub.TrangThai = "Thành công";
                            Results.Remove(stub);
                            AddEnvelopeRows(item.SourcePath, envelope);
                        });
                        ok = true;
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            _errorLog.LogException("gcn-new", "StartAsync.ProcessFileCanceled:" + Path.GetFileName(item.SourcePath), ex);
                            AddFailedSourcePath(item.SourcePath);
                        }
                        Ui(() => stub.TrangThai = ct.IsCancellationRequested
                            ? "Đã dừng"
                            : "❌ " + UserFacingError.ProcessFile(ex));
                    }
                    catch (Exception ex)
                    {
                        _errorLog.LogException("gcn-new", "StartAsync.ProcessFile:" + Path.GetFileName(item.SourcePath), ex);
                        AddFailedSourcePath(item.SourcePath);
                        Ui(() => stub.TrangThai = "❌ " + UserFacingError.ProcessFile(ex));
                    }
                    finally
                    {
                        // Bắt buộc: chốt trong finally để không dòng nào treo ở "Đang xử lý".
                        queue.Settle(item, ok);
                    }
                }
            }
            catch (Exception ex)
            {
                // Một worker chết không được kéo theo phần việc còn lại.
                _errorLog.LogException("gcn-new", "StartAsync.ConsumerLoop", ex);
            }
        })).ToList();

        await Task.WhenAll(consumers);

        // Chốt phiên trong finally: nếu bước tổng kết/báo cáo có lỗi thì IsRunning vẫn phải về false,
        // nếu không cả Start lẫn Export đều bị khóa vĩnh viễn cho tới khi khởi động lại app. Toàn bộ
        // phần chờ/dọn dẹp hàng đợi upload bên dưới cũng nằm TRONG khối try này vì cùng lý do đó.
        try
        {
            // Chờ producer dừng hẳn TRƯỚC khi tổng kết: nếu không, người dùng bấm Dừng có thể khiến
            // producer chốt muộn (sau khi phần tổng kết bên dưới đã ghi "Hoàn tất" + ép 100%) rồi ghi
            // đè ngược tiến độ/trạng thái đã hiển thị xong.
            await queue.Completion;

            // Bấm Dừng giữa chừng có thể để lại vài nguồn đã publish xong nhưng chưa consumer nào kịp
            // claim (ClaimNextAsync hủy ngay dù còn việc đọc được) -> chốt nốt để "done" đủ tổng số
            // file. Gọi TRƯỚC vòng quét Failures để OnItemSettled (đếm done/success/failed) chạy kịp
            // trước khi tổng kết phiên bên dưới. SettleRemaining trả về ĐÚNG danh sách nguồn vừa chốt —
            // đây là nguồn sự thật duy nhất để biết nguồn nào "không ai xử lý". TUYỆT ĐỐI không được
            // suy đoán qua chuỗi TrangThai hiển thị: từng có bug nghiêm trọng vì stub gốc trong stubMap
            // không được cập nhật lại khi consumer xử lý xong (AddEnvelopeRows tạo dòng lưới MỚI chứ
            // không sửa lại stub), khiến MỌI file thành công bị vòng quét theo chuỗi nhận nhầm là "còn treo".
            var abandonedSources = queue.SettleRemaining(false, "Đã dừng");

            // Ép ProgressValue = 100 NGAY sau khi mọi nguồn đã chốt xong (Completion + SettleRemaining ở
            // trên), TRƯỚC hai vòng quét cập nhật lưới bên dưới: Ui(...) chạy đồng bộ khi gọi từ UI thread,
            // một property setter bị bind lỡ ném lỗi trong hai vòng đó sẽ nhảy thẳng ra catch bên dưới và bỏ
            // qua dòng này — khiến CanExport (đòi ProgressValue >= 100) khoá Export vĩnh viễn cho phiên đó.
            if (files.Count > 0)
            {
                ProgressValue = 100;
            }

            // File hỏng ở phía upload không bao giờ tới consumer -> cập nhật dòng lưới cho chúng.
            foreach (var failure in queue.Failures)
            {
                if (!stubMap.TryGetValue(failure.SourcePath, out var stub)) continue;
                Ui(() => stub.TrangThai = ct.IsCancellationRequested
                    ? "Đã dừng"
                    : "❌ " + UserFacingError.ProcessFileFallback);
                if (!ct.IsCancellationRequested)
                {
                    _errorLog.LogException("gcn-new", "StartAsync.UploadFailed:" + Path.GetFileName(failure.SourcePath),
                        new InvalidOperationException(failure.Reason));
                    AddFailedSourcePath(failure.SourcePath);
                }
            }

            // Nguồn đã publish xong nhưng chưa consumer nào kịp nhận trước khi bị dừng — chỉ cập nhật
            // ĐÚNG những dòng SettleRemaining vừa chốt ở trên, không đụng tới bất kỳ stub nào khác.
            foreach (var sourcePath in abandonedSources)
            {
                if (!stubMap.TryGetValue(sourcePath, out var stub)) continue;

                if (ct.IsCancellationRequested)
                {
                    Ui(() => stub.TrangThai = "Đã dừng");
                }
                else
                {
                    // Trường hợp hiếm: không phải do người dùng dừng mà vẫn còn nguồn không ai xử lý.
                    _errorLog.LogException("gcn-new", "StartAsync.StuckItem:" + Path.GetFileName(sourcePath),
                        new InvalidOperationException("File còn dang dở sau khi luồng xử lý đã kết thúc."));
                    AddFailedSourcePath(sourcePath);
                    Ui(() => stub.TrangThai = "❌ " + UserFacingError.ProcessFileFallback);
                }
            }

            _lastRunCompletedAt = DateTime.Now;
            int envelopeCount;
            lock (_envelopesLock)
            {
                envelopeCount = _envelopes.Count;
            }

            StatusMessage = envelopeCount > 0
                ? $"Hoàn tất: {success} thành công, {failed} lỗi. Bấm Export để xuất Excel."
                : $"Hoàn tất: {success} thành công, {failed} lỗi. Không có dữ liệu để xuất.";

            List<NewGcnEnvelope> reportSnapshot;
            lock (_envelopesLock)
            {
                reportSnapshot = _envelopes.ToList();
            }
            var rowCount = reportSnapshot.Sum(CountDisplayRows);
            _notifier.NotifyExport(
                Title,
                "Hoàn tất luồng OCR GCN iLIS",
                _lastRunStartedAt,
                _lastRunCompletedAt,
                rowCount,
                SelectedPath ?? "",
                new[]
                {
                    new ExportReportItem { Name = "Số file đã chọn", Value = files.Count.ToString() },
                    new ExportReportItem { Name = "Số GCN đọc thành công", Value = envelopeCount.ToString() },
                    new ExportReportItem { Name = "Số file lỗi", Value = failed.ToString() },
                    new ExportReportItem { Name = "Số dòng dữ liệu đọc được", Value = rowCount.ToString() },
                    new ExportReportItem { Name = "Kết quả", Value = StatusMessage ?? "" }
                });
        }
        catch (Exception ex)
        {
            _errorLog.LogException("gcn-new", "StartAsync.Finalize", ex);
        }
        finally
        {
            IsRunning = false;
            ExportCommand.NotifyCanExecuteChanged();
            ExportFailedSourcesCommand.NotifyCanExecuteChanged();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Mỗi thửa (danh_sach_dong) → 1 dòng lưới.</summary>
    private void AddEnvelopeRows(string file, NewGcnEnvelope envelope)
    {
        // Hiển thị đường dẫn tương đối từ thư mục đã chọn (đã tính sẵn trong SelectedFiles).
        var displayName = SelectedFiles.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))?.Name
                          ?? Path.GetFileName(file);
        var info = envelope.thong_tin_gcn;
        var chu = string.Join("; ", (info.chu_su_dung_chi_tiet ?? new()).Select(c => c.ho_ten ?? ""));
        var rows = envelope.danh_sach_dong ?? new();
        if (rows.Count == 0)
        {
            Results.Add(new NewGcnRecord
            {
                FilePath = file,
                FileName = displayName,
                TrangThai = "Thành công - không có dòng thửa đất",
                SoSerial = info.so_serial ?? "",
                ChuSuDung = chu,
                DoTinCay = info.do_tin_cay ?? ""
            });
            return;
        }

        foreach (var r in rows)
        {
            Results.Add(new NewGcnRecord
            {
                FilePath = file,
                FileName = displayName,
                TrangThai = "Thành công",
                SoSerial = info.so_serial ?? "",
                ChuSuDung = chu,
                SoThua = r.td_so_thua ?? "",
                SoTo = r.td_so_to ?? "",
                TongDienTich = r.td_tong_dien_tich ?? "",
                LoaiDat = r.muc_dich_su_dung?.FirstOrDefault()?.ma_mdsd ?? "",
                DoTinCay = info.do_tin_cay ?? ""
            });
        }
    }

    /// <summary>Dựng byte gửi Gemini cho một file nguồn — nhánh PDF gửi nguyên file, nhánh ảnh render từng trang.</summary>
    private async Task<IReadOnlyList<UploadArtifact>> BuildArtifactsAsync(string filePath, CancellationToken ct)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".pdf" && !_opt.OptimizeImages)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            return new[] { new UploadArtifact("source-pdf", fileName, "application/pdf", bytes) };
        }

        var images = ext == ".pdf"
            ? (await _pdf.RenderPagesJpegAsync(filePath, ct)).ToList()
            : new List<byte[]> { await File.ReadAllBytesAsync(filePath, ct) };
        if (images.Count == 0) throw new Exception("Không có ảnh nào được kết xuất từ file.");

        // Đuôi ".pdf" (đã render ra JPEG thô) rơi vào nhánh mặc định "image/jpeg" của ImageMimeTypes —
        // khớp đúng thực tế render. Dùng chung bảng ánh xạ với NewGcnExtractService để không lệch nhãn
        // MIME khi upload (đặc biệt TIFF: trước đây bị gán cứng "image/jpeg" + đuôi ".jpg" là sai).
        var mime = ImageMimeTypes.FromExtension(ext);
        var displayExt = ImageMimeTypes.ToExtension(mime);
        return images
            .Select((img, i) => new UploadArtifact(
                $"page-{i + 1:D4}",
                $"{Path.GetFileNameWithoutExtension(fileName)}-page-{i + 1:D4}{displayExt}",
                mime,
                img))
            .ToList();
    }

    private static int CountDisplayRows(NewGcnEnvelope envelope)
    {
        var rows = envelope.danh_sach_dong?.Count ?? 0;
        return rows == 0 ? 1 : rows;
    }

    [RelayCommand]
    private void Pause() => _cts?.Cancel();

    private bool CanExport()
    {
        lock (_envelopesLock)
        {
            return !IsRunning && ProgressValue >= 100 && _envelopes.Count > 0;
        }
    }

    private bool CanExportFailedSources() => !IsRunning && SnapshotFailedSourcePaths().Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportFailedSources))]
    private async Task ExportFailedSourcesAsync()
    {
        var failedPaths = SnapshotFailedSourcePaths();
        if (failedPaths.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return;

        try
        {
            var copied = await Task.Run(
                () => FailedSourceFileExporter.CopyFiles(failedPaths, destDir, SelectedPath),
                CancellationToken.None);
            StatusMessage = $"Đã xuất {copied} file lỗi vào: {destDir}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException("gcn-new", "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        List<NewGcnEnvelope> snapshot;
        List<SuccessfulGcnSource> successfulSources;
        lock (_envelopesLock)
        {
            snapshot = _envelopes.ToList();
            successfulSources = _successfulSources
                .Select(kv => new SuccessfulGcnSource(kv.Key, kv.Value))
                .ToList();
        }
        if (snapshot.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outPath = Path.Combine(destDir, $"GCN-New-output_{stamp}.xlsx");
            var rowCount = await Task.Run(
                () => _excel.Write(snapshot, outPath, _opt.TemplateExcel),
                CancellationToken.None);
            var renameResult = await Task.Run(
                () => RenameSuccessfulSourceFiles(successfulSources),
                CancellationToken.None);
            await RecordOcrCreditAsync(Path.GetFileName(outPath), outPath, rowCount);
            StatusMessage = $"Đã xuất {rowCount} dòng dữ liệu từ {snapshot.Count} GCN → {outPath}. Đổi tên {renameResult.Renamed} file nguồn.";
            _folderPicker.OpenFolder(destDir);
            DeleteGeminiFilesInBackground(successfulSources.Select(x => x.SourcePath));
        }
        catch (Exception ex)
        {
            _errorLog.LogException("gcn-new", "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private async Task RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang)
    {
        var result = await _auth.RecordOcrCreditAsync(tenFile, duongDanFile, soTrang, CancellationToken.None);
        if (result.IsSuccess) return;

        var error = result.Error ?? "Không ghi nhận được hạn mức xử lý.";
        _errorLog.LogException("gcn-new", "ExportAsync.RecordOcrCredit", new InvalidOperationException(error));
        ErrorMessage = error;
    }

    private void AddFailedSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_failedSourcePathsLock)
        {
            _failedSourcePaths.Add(path);
        }
    }

    private void ClearFailedSourcePaths()
    {
        lock (_failedSourcePathsLock)
        {
            _failedSourcePaths.Clear();
        }

        ExportFailedSourcesCommand.NotifyCanExecuteChanged();
    }

    private List<string> SnapshotFailedSourcePaths()
    {
        lock (_failedSourcePathsLock)
        {
            return _failedSourcePaths.ToList();
        }
    }

    private void DeleteGeminiFilesInBackground(IEnumerable<string> sourcePaths)
    {
        var paths = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _geminiFiles.DeleteBySourcePathsAsync(paths, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _errorLog.LogException("gcn-new", "ExportAsync.DeleteGeminiFiles", ex);
            }
        });
    }

    /// <summary>
    /// Gán tên file đích cho từng file nguồn, có chống trùng số serial trên TOÀN LÔ (kể cả hai file nằm
    /// ở hai thư mục khác nhau).
    ///
    /// Phân biệt hai ca dễ lẫn: "1 giấy chứng nhận nhiều thửa" chỉ sinh ĐÚNG MỘT mục ở đây (các thửa
    /// nằm trong cùng một envelope) nên không bao giờ bị đánh hậu tố; còn hai FILE NGUỒN khác nhau đọc
    /// ra cùng serial thì file đầu giữ tên trơn <c>{serial}-GCN.pdf</c>, các file sau lần lượt
    /// <c>{serial}_1-GCN.pdf</c>, <c>{serial}_2-GCN.pdf</c>…
    ///
    /// Thứ tự xét theo ĐƯỜNG DẪN NGUỒN A→Z chứ không theo thứ tự OCR xong: worker chạy song song nên
    /// thứ tự hoàn thành mỗi lần một khác, lấy theo đó thì chạy lại cùng một lô sẽ ra tên file khác nhau.
    /// Để internal static cho test harness gọi được mà không phải dựng cả ViewModel.
    /// </summary>
    internal static Dictionary<string, string> BuildRenamePlan(IEnumerable<SuccessfulGcnSource> sources)
    {
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var groups = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourcePath))
            .GroupBy(
                source => NormalizeSerialForFileName(source.Envelope.thong_tin_gcn.so_serial),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            // Thiếu serial thì không đổi tên được; nhánh gọi sẽ ghi log và bỏ qua.
            if (string.IsNullOrWhiteSpace(group.Key)) continue;

            int index = 0;
            foreach (var source in group.OrderBy(s => s.SourcePath, StringComparer.OrdinalIgnoreCase))
            {
                plan[source.SourcePath] = index == 0
                    ? $"{group.Key}-GCN.pdf"
                    : $"{group.Key}_{index}-GCN.pdf";
                index++;
            }
        }

        return plan;
    }

    private RenameSourceResult RenameSuccessfulSourceFiles(IReadOnlyCollection<SuccessfulGcnSource> sources)
    {
        int renamed = 0, alreadyNamed = 0, skipped = 0;
        var plan = BuildRenamePlan(sources);

        foreach (var source in sources)
        {
            try
            {
                if (!plan.TryGetValue(source.SourcePath, out var targetFileName))
                {
                    skipped++;
                    _errorLog.LogException("gcn-new", "ExportAsync.RenameSource:" + Path.GetFileName(source.SourcePath),
                        new InvalidOperationException("Không đổi tên file nguồn vì thiếu số serial."));
                    continue;
                }

                if (!File.Exists(source.SourcePath))
                {
                    skipped++;
                    _errorLog.LogException("gcn-new", "ExportAsync.RenameSource:" + Path.GetFileName(source.SourcePath),
                        new FileNotFoundException("Không tìm thấy file nguồn để đổi tên.", source.SourcePath));
                    continue;
                }

                string? dir = Path.GetDirectoryName(Path.GetFullPath(source.SourcePath));
                if (string.IsNullOrWhiteSpace(dir))
                {
                    skipped++;
                    _errorLog.LogException("gcn-new", "ExportAsync.RenameSource:" + Path.GetFileName(source.SourcePath),
                        new InvalidOperationException("Không xác định được thư mục chứa file nguồn."));
                    continue;
                }

                string targetPath = Path.Combine(dir, targetFileName);
                if (string.Equals(Path.GetFullPath(source.SourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    alreadyNamed++;
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    skipped++;
                    _errorLog.LogException("gcn-new", "ExportAsync.RenameSource:" + Path.GetFileName(source.SourcePath),
                        new IOException("Không đổi tên file nguồn vì file đích đã tồn tại: " + targetPath));
                    continue;
                }

                File.Move(source.SourcePath, targetPath);
                source.Envelope.ten_file = Path.GetFileName(targetPath);
                renamed++;
            }
            catch (Exception ex)
            {
                skipped++;
                _errorLog.LogException("gcn-new", "ExportAsync.RenameSource:" + Path.GetFileName(source.SourcePath), ex);
            }
        }

        return new RenameSourceResult(renamed, alreadyNamed, skipped);
    }

    private static string NormalizeSerialForFileName(string? serial)
    {
        var normalized = string.Join(" ", (serial ?? "")
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized)) return "";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '_');
        return normalized.Trim();
    }

    private static string Eta(DateTime start, int done, int total)
    {
        var elapsed = DateTime.Now - start;
        if (done <= 0 || elapsed.TotalSeconds <= 1) return "";
        double speed = done / elapsed.TotalSeconds;
        var remain = TimeSpan.FromSeconds((total - done) / speed);
        return remain.TotalMinutes >= 1
            ? $"  |  ~{Math.Round(remain.TotalMinutes)} phút còn lại"
            : $"  |  ~{Math.Round(remain.TotalSeconds)} giây còn lại";
    }

    /// <summary>
    /// Chạy <paramref name="action"/> theo đúng ngữ nghĩa "trên UI thread": các lời gọi KHÔNG bao giờ
    /// chồng lấn nhau. Có dispatcher thì hàng đợi UI tự lo tuần tự hoá.
    ///
    /// Nhánh <c>_dispatcher is null</c> (chỉ xảy ra khi chạy ngoài WinUI, VD test harness) trước đây gọi
    /// thẳng <c>action()</c> trên chính thread nền đang gọi — nhiều worker cùng lúc sẽ sửa song song
    /// <see cref="Results"/> (ObservableCollection KHÔNG thread-safe) và ném
    /// <see cref="ArgumentOutOfRangeException"/> giữa luồng xử lý, khiến file OCR THÀNH CÔNG bị nhánh
    /// catch đánh dấu là file lỗi. Khoá lại để nhánh này giữ đúng bất biến như nhánh có dispatcher.
    /// Monitor có tính tái nhập nên <c>Ui()</c> lồng nhau trên cùng thread vẫn an toàn.
    /// </summary>
    private void Ui(Action action)
    {
        if (_dispatcher is null) { lock (_uiGate) action(); }
        else if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    /// <summary>Khoá tuần tự hoá cho nhánh không có dispatcher (xem <see cref="Ui"/>).</summary>
    private readonly object _uiGate = new();
}
