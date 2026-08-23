using System;
using System.Collections.Concurrent;
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
using OCR.Business.Notifications;
using OCR.Business.Split;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "OCR Tách GCN": chọn thư mục PDF → gọi API tách thành bộ GCN/GT/GTK ghi ra thư mục tạm →
/// Export copy từ thư mục tạm vào thư mục đã chọn (port từ split_gcn.py).
/// </summary>
public partial class TachGcnViewModel : ObservableObject
{
    private readonly IFolderPickerService _folderPicker;
    private readonly ISplitGcnService _split;
    private readonly ISplitRunCacheService _cache;
    private readonly ISplitCachePromptService _cachePrompt;
    private readonly IExportResultNotifier _notifier;
    private readonly IErrorLogService _errorLog;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly IGeminiUploadPipeline _uploadPipeline;
    private readonly IAuthService _auth;
    private readonly SplitGcnOptions _opt;
    private readonly SplitGcnVariant _variant;
    private readonly bool _copyGcnToOcrOnExport;
    private readonly string _screenKey;
    private readonly DispatcherQueue? _dispatcher;

    private CancellationTokenSource? _cts;
    private SplitRunState? _runState;
    private SplitRunWorkspace? _workspace;
    private DateTime _lastRunStartedAt = DateTime.Now;
    private DateTime _lastRunCompletedAt = DateTime.Now;
    private readonly HashSet<string> _failedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedSourcePathsLock = new();

    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportFailedSourcesCommand))]
    [NotifyPropertyChangedFor(nameof(CanChangeOptions))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double _progressValue;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _selectedPath;
    [ObservableProperty] private bool _normalizePageRotation;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool CanChangeOptions => !IsRunning;
    public string ProgressText => $"{ProgressValue:0}%";
    public string FileCountText => $"Tìm thấy {SelectedFiles.Count} tệp PDF";

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<SplitGcnRecord> Results { get; } = new();

    public TachGcnViewModel(
        IFolderPickerService folderPicker,
        ISplitGcnService split,
        ISplitRunCacheService cache,
        ISplitCachePromptService cachePrompt,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        SplitGcnOptions opt)
        : this(
            folderPicker,
            split,
            cache,
            cachePrompt,
            notifier,
            errorLog,
            geminiFiles,
            uploadPipeline,
            auth,
            opt,
            SplitGcnVariant.Standard,
            "tach-gcn",
            "OCR Tách GCN",
            "Chọn thư mục chứa PDF gốc → tách thành các bộ GCN/GT/GTK. Sau khi hoàn tất, bấm Export để lưu kết quả vào thư mục đã chọn.")
    {
    }

    protected TachGcnViewModel(
        IFolderPickerService folderPicker,
        ISplitGcnService split,
        ISplitRunCacheService cache,
        ISplitCachePromptService cachePrompt,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        SplitGcnOptions opt,
        SplitGcnVariant variant,
        string screenKey,
        string title,
        string description,
        bool copyGcnToOcrOnExport = true)
    {
        _folderPicker = folderPicker;
        _split = split;
        _cache = cache;
        _cachePrompt = cachePrompt;
        _notifier = notifier;
        _errorLog = errorLog;
        _geminiFiles = geminiFiles;
        _uploadPipeline = uploadPipeline;
        _auth = auth;
        _opt = opt;
        _variant = variant;
        _screenKey = screenKey;
        _copyGcnToOcrOnExport = copyGcnToOcrOnExport;
        Title = title;
        Description = description;
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
            // Chỉ định danh workspace; việc hỏi dùng lại dữ liệu cũ dời sang lúc bấm Start.
            var workspace = _cache.GetWorkspace(_screenKey, path);
            ResetSelection();
            AddFolder(path);
            SelectedPath = path;
            _workspace = workspace;
        }
        catch (Exception ex)
        {
            _errorLog.LogException(_screenKey, "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            foreach (var file in _folderPicker.EnumerateFiles(folder, new[] { ".pdf" }, recursive: true))
            {
                if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
                // Name = đường dẫn tương đối từ thư mục đã chọn (quét đệ quy cả thư mục con).
                SelectedFiles.Add(new SelectedFile(Path.GetRelativePath(folder, file), file));
            }
        }
        catch (Exception ex)
        {
            _errorLog.LogException(_screenKey, "AddFolder", ex);
            ErrorMessage = UserFacingError.ScanFolder(ex);
        }
    }

    private void ResetSelection()
    {
        SelectedFiles.Clear();
        Results.Clear();
        ProgressValue = 0;
        _runState = null;
        ClearFailedSourcePaths();
        StatusMessage = null;
    }

    private bool CanStart() => !IsRunning && SelectedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedFiles.Count == 0 || _workspace is null) return;

        // Kiểm tra dữ liệu quét cũ tại thời điểm Start: còn cache JSON thì hỏi dùng lại / quét lại / hủy.
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
            _errorLog.LogException(_screenKey, "StartAsync.PrepareCache", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
            return;
        }

        ErrorMessage = null;
        Results.Clear();
        ClearFailedSourcePaths();
        IsRunning = true;
        ProgressValue = 0;
        _lastRunStartedAt = DateTime.Now;
        _lastRunCompletedAt = _lastRunStartedAt;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            await _cache.PrepareOutputAsync(_workspace, ct);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(_screenKey, "StartAsync.PrepareOutput", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
            return;
        }
        var workspace = _workspace;
        var allocator = new LabelAllocator(workspace.OutputDir);

        var selected = SelectedFiles.ToList();
        var files = selected.Select(f => f.Path).ToList();
        _runState = new SplitRunState(files.Count);
        var runState = _runState;
        var recordMap = new Dictionary<string, SplitGcnRecord>();
        foreach (var sf in selected)
        {
            var stub = new SplitGcnRecord { FilePath = sf.Path, FileName = sf.Name, TrangThai = "Chờ xử lý" };
            Results.Add(stub);
            recordMap[sf.Path] = stub;
        }

        int workers = Math.Max(1, _opt.Workers);
        int success = 0, failed = 0;
        int totalGcnCount = 0, totalFilesCreated = 0, totalCompleteSetCount = 0, totalPagesRotated = 0;
        var missingFileFolders = new ConcurrentBag<string>();
        // "ok" (Settle) chỉ nghĩa là SplitAsync không ném exception — KHÔNG đồng nghĩa "có tạo ra file".
        // Ghi riêng ngữ nghĩa "có output" theo từng nguồn để OnItemSettled đọc lại đúng giá trị khi gọi
        // MarkFileCompleted, tách bạch khỏi "ok" (chỉ dùng để đếm success/failed hiển thị).
        var hasOutputBySource = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var startTime = DateTime.Now;

        // Chốt sổ tại ĐÚNG MỘT chỗ: mọi thay đổi tiến độ đi qua đây.
        // Truyền qua OnItemSettled trong request — KHÔNG tự "+=" vào queue.ItemSettled sau khi
        // Start trả về: producer chạy NGAY bên trong Start, nguồn nào chốt trước khi kịp "+=" sẽ
        // làm mất sự kiện, tiến độ không bao giờ đủ tổng số file và ProgressValue kẹt dưới 100.
        var queue = _uploadPipeline.Start(new GeminiUploadRequest
        {
            SourcePaths = files,
            WillHitJsonCache = path => useCachedJson && _cache.HasFreshJson(workspace, path),
            PrepareArtifactsAsync = async (path, token) => new[]
            {
                new UploadArtifact("source-pdf", Path.GetFileName(path), "application/pdf",
                    await File.ReadAllBytesAsync(path, token))
            },
            OnItemUploading = sourcePath => Ui(() =>
            {
                if (recordMap.TryGetValue(sourcePath, out var stub))
                    stub.TrangThai = "⏳ Đang tải lên...";
            }),
            OnItemSettled = (sourcePath, ok) =>
            {
                if (ok) Interlocked.Increment(ref success);
                else Interlocked.Increment(ref failed);
                // Chốt sổ chỉ ở đây: SplitRunState tự đếm bằng MarkFileCompleted, CanExport dựa vào
                // cờ _isCompleted riêng của nó, không đi qua ProgressValue.
                // "ok" chỉ nghĩa là không lỗi — số file thực sự tạo ra do consumer ghi riêng vào
                // hasOutputBySource ngay trước khi Settle; nguồn nào không có mặt ở đó (upload hỏng,
                // bị bỏ rơi khi Dừng) coi là không có output.
                var hasOutput = hasOutputBySource.TryGetValue(sourcePath, out var producedOutput) && producedOutput;
                runState.MarkFileCompleted(hasOutput: hasOutput);
                int d = runState.CompletedFiles;

                Ui(() =>
                {
                    ProgressValue = runState.ProgressValue;
                    StatusMessage = $"Đang tách: {d}/{files.Count}  |  ✅ {success}  ❌ {failed}{Eta(startTime, d, files.Count)}";
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

                    var rec = recordMap[item.SourcePath];
                    var ok = false;
                    var hasOutput = false;
                    try
                    {
                        Ui(() => rec.TrangThai = NormalizePageRotation ? "⏳ Đang tách/xoay..." : "⏳ Đang tách...");
                        var result = await _split.SplitAsync(
                            item.SourcePath, allocator, NormalizePageRotation, _variant,
                            _cache.GetJsonPath(workspace, item.SourcePath), useCachedJson, ct, item.Files);

                        Interlocked.Add(ref totalGcnCount, result.GcnCount);
                        Interlocked.Add(ref totalFilesCreated, result.FilesCreated);
                        Interlocked.Add(ref totalCompleteSetCount, result.CompleteSetCount);
                        Interlocked.Add(ref totalPagesRotated, result.PagesRotated);
                        foreach (var missing in result.MissingFileFolders)
                        {
                            if (!string.IsNullOrWhiteSpace(missing))
                                missingFileFolders.Add(missing);
                        }

                        Ui(() =>
                        {
                            rec.SoLuongGcn = result.GcnCount;
                            rec.SoFileTao = result.FilesCreated;
                            rec.SoBoHoanChinh = result.CompleteSetCount;
                            rec.ThieuFile = string.Join(", ", result.MissingFileFolders);
                            rec.TrangThai = NormalizePageRotation && result.PagesRotated > 0
                                ? $"Hoàn thành ({result.FilesCreated} file, xoay {result.PagesRotated} trang)"
                                : $"Hoàn thành ({result.FilesCreated} file)";
                        });
                        ok = true;
                        // "Không ném exception" khác "có tạo ra file": một PDF hợp lệ nhưng AI không nhận
                        // diện được tài liệu nào vẫn trả FilesCreated = 0 mà không ném lỗi (xem
                        // SplitGcnService.SplitFromResultAsync) — không được coi là "có output".
                        hasOutput = result.FilesCreated > 0;
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            _errorLog.LogException(_screenKey, "StartAsync.SplitCanceled:" + Path.GetFileName(item.SourcePath), ex);
                            AddFailedSourcePath(item.SourcePath);
                        }
                        Ui(() =>
                        {
                            rec.ErrorMessage = ct.IsCancellationRequested ? "" : UserFacingError.ProcessFile(ex);
                            rec.TrangThai = ct.IsCancellationRequested
                                ? "Đã dừng"
                                : "❌ " + rec.ErrorMessage;
                        });
                    }
                    catch (Exception ex)
                    {
                        _errorLog.LogException(_screenKey, "StartAsync.Split:" + Path.GetFileName(item.SourcePath), ex);
                        AddFailedSourcePath(item.SourcePath);
                        Ui(() =>
                        {
                            var message = UserFacingError.ProcessFile(ex);
                            rec.ErrorMessage = message;
                            rec.TrangThai = "❌ " + message;
                        });
                    }
                    finally
                    {
                        // Ghi output-ness TRƯỚC khi chốt: OnItemSettled đọc lại giá trị này ngay khi Settle
                        // bắn sự kiện đồng bộ bên dưới, để MarkFileCompleted nhận đúng "có tạo ra file" thay
                        // vì "không lỗi". Bắt buộc: chốt trong finally để không dòng nào treo ở "Đang tách...".
                        hasOutputBySource[item.SourcePath] = hasOutput;
                        queue.Settle(item, ok);
                    }
                }
            }
            catch (Exception ex)
            {
                // Một worker chết không được kéo theo phần việc còn lại.
                _errorLog.LogException(_screenKey, "StartAsync.ConsumerLoop", ex);
            }
        })).ToList();

        await Task.WhenAll(consumers);

        // Chốt phiên trong finally: nếu bước tổng kết/báo cáo có lỗi thì IsRunning vẫn phải về false,
        // nếu không cả Start lẫn Export đều bị khóa vĩnh viễn cho tới khi khởi động lại app. Toàn bộ
        // phần chờ/dọn dẹp hàng đợi upload bên dưới cũng nằm TRONG khối try này vì cùng lý do đó.
        try
        {
            // Chờ producer dừng hẳn TRƯỚC khi tổng kết: nếu không, người dùng bấm Dừng có thể khiến
            // producer chốt muộn (sau khi phần tổng kết bên dưới đã đóng phiên) rồi ghi đè ngược
            // tiến độ/trạng thái đã hiển thị xong.
            await queue.Completion;

            // Bấm Dừng giữa chừng có thể để lại vài nguồn đã publish xong nhưng chưa consumer nào kịp
            // claim (ClaimNextAsync hủy ngay dù còn việc đọc được) -> chốt nốt để tiến độ đủ tổng số
            // file. SettleRemaining trả về ĐÚNG danh sách nguồn vừa chốt — đây là nguồn sự thật duy
            // nhất để biết nguồn nào "không ai xử lý". TUYỆT ĐỐI không được suy đoán qua chuỗi
            // TrangThai hiển thị.
            var abandonedSources = queue.SettleRemaining(false, "Đã dừng");

            // Chốt trạng thái phiên NGAY sau khi mọi nguồn đã chốt xong (Completion + SettleRemaining ở
            // trên), TRƯỚC hai vòng quét cập nhật lưới bên dưới: Ui(...) chạy đồng bộ khi gọi từ UI thread,
            // một property setter bị bind lỡ ném lỗi trong hai vòng đó sẽ nhảy thẳng ra catch bên dưới và bỏ
            // qua dòng này — khiến _isCompleted không bao giờ được set, khoá Export vĩnh viễn cho phiên đó.
            runState.MarkRunCompleted(ct.IsCancellationRequested);

            _lastRunCompletedAt = DateTime.Now;
            ProgressValue = runState.ProgressValue;

            var hasData = Results.Any(IsDone);
            StatusMessage = hasData
                ? $"Hoàn tất: {success} thành công, {failed} lỗi. Bấm Export để lưu vào thư mục đã chọn."
                : $"Hoàn tất: {success} thành công, {failed} lỗi. Không có file nào được tách.";

            var reportItems = new List<ExportReportItem>
            {
                new() { Name = "Số PDF nguồn xử lý", Value = files.Count.ToString() },
                new() { Name = "Số PDF nguồn thành công", Value = success.ToString() },
                new() { Name = "Số PDF nguồn lỗi", Value = failed.ToString() },
                new() { Name = "Số PDF kết quả", Value = totalFilesCreated.ToString() },
                new()
                {
                    Name = _variant == SplitGcnVariant.NoGcn
                        ? "Số bộ GT/GTK tách thành công"
                        : "Số bộ tách thành công",
                    Value = totalCompleteSetCount.ToString()
                },
                new() { Name = "Kết quả", Value = StatusMessage ?? "" }
            };

            if (_variant != SplitGcnVariant.NoGcn)
            {
                reportItems.Insert(4, new ExportReportItem
                {
                    Name = "Số GCN nhận diện",
                    Value = totalGcnCount.ToString()
                });
            }

            if (NormalizePageRotation)
            {
                reportItems.Add(new ExportReportItem
                {
                    Name = "Số trang đã xoay",
                    Value = totalPagesRotated.ToString()
                });
            }

            if (_variant == SplitGcnVariant.New)
            {
                var missing = string.Join("; ", missingFileFolders
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                reportItems.Add(new ExportReportItem
                {
                    Name = "Thư mục thiếu file",
                    Value = string.IsNullOrWhiteSpace(missing) ? "Không" : missing
                });
            }

            // NotifyExport + RecordOcrCreditAsync đặt CÙNG chỗ với MarkRunCompleted ở trên, TRƯỚC hai vòng
            // quét cập nhật lưới bên dưới — cùng lý do: nếu một property setter bind lỡ ném lỗi trong hai
            // vòng quét đó, luồng nhảy thẳng ra catch bên dưới. Đặt SAU hai vòng quét thì Export vẫn được
            // mở khoá (vì MarkRunCompleted đã chạy ở trên) nhưng việc trừ quota và gửi báo cáo Telegram bị
            // bỏ qua HOÀN TOÀN im lặng — không log, không InfoBar, chủ dự án không hề biết.
            _notifier.NotifyExport(
                Title,
                NormalizePageRotation
                    ? $"Hoàn tất luồng {Title} có xoay thẳng trang giấy"
                    : $"Hoàn tất luồng {Title}",
                _lastRunStartedAt,
                _lastRunCompletedAt,
                totalFilesCreated,
                SelectedPath ?? workspace.OutputDir,
                reportItems);

            // Quota màn Tách ghi nhận theo số file tách ra, chốt ngay khi chạy xong 100% — không đợi Export.
            // Bấm Dừng giữa chừng thì KHÔNG ghi ở đây: phiên bị huỷ dở nên Export bị khoá luôn (CanExport
            // = false khi canceled), số file đã tách nằm kẹt trong workspace tạm không ai lấy được — nếu vẫn
            // trừ quota ở đây thì lần chạy lại sau (chọn "dùng dữ liệu cũ") sẽ bị tính tiền lần thứ hai.
            if (!ct.IsCancellationRequested)
            {
                await RecordOcrCreditAsync(
                    GetOutputDisplayName(SelectedPath ?? ""), SelectedPath ?? "", totalFilesCreated);
            }

            // Từ đây trở xuống chỉ còn việc dọn hiển thị lưới cho các nguồn không đi qua consumer — quota
            // và báo cáo Telegram đã được đảm bảo chạy ở trên, một lỗi ở hai vòng quét này không còn kéo
            // theo hệ quả đó nữa.

            // File hỏng ở phía upload không bao giờ tới consumer -> cập nhật dòng lưới cho chúng.
            foreach (var failure in queue.Failures)
            {
                if (!recordMap.TryGetValue(failure.SourcePath, out var rec)) continue;
                Ui(() => rec.TrangThai = ct.IsCancellationRequested
                    ? "Đã dừng"
                    : "❌ " + UserFacingError.ProcessFileFallback);
                if (!ct.IsCancellationRequested)
                {
                    _errorLog.LogException(_screenKey, "StartAsync.UploadFailed:" + Path.GetFileName(failure.SourcePath),
                        new InvalidOperationException(failure.Reason));
                    AddFailedSourcePath(failure.SourcePath);
                }
            }

            // Nguồn đã publish xong nhưng chưa consumer nào kịp nhận trước khi bị dừng — chỉ cập nhật
            // ĐÚNG những dòng SettleRemaining vừa chốt ở trên, không đụng tới bất kỳ dòng nào khác.
            foreach (var sourcePath in abandonedSources)
            {
                if (!recordMap.TryGetValue(sourcePath, out var rec)) continue;

                if (ct.IsCancellationRequested)
                {
                    Ui(() => rec.TrangThai = "Đã dừng");
                }
                else
                {
                    // Trường hợp hiếm: không phải do người dùng dừng mà vẫn còn nguồn không ai xử lý.
                    _errorLog.LogException(_screenKey, "StartAsync.StuckItem:" + Path.GetFileName(sourcePath),
                        new InvalidOperationException("File còn dang dở sau khi luồng xử lý đã kết thúc."));
                    AddFailedSourcePath(sourcePath);
                    Ui(() => rec.TrangThai = "❌ " + UserFacingError.ProcessFileFallback);
                }
            }
        }
        catch (Exception ex)
        {
            _errorLog.LogException(_screenKey, "StartAsync.Finalize", ex);
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

    [RelayCommand]
    private void Pause() => _cts?.Cancel();

    private static bool IsDone(SplitGcnRecord r) =>
        r.TrangThai != "Chờ xử lý" &&
        r.TrangThai != "⏳ Đang tải lên..." &&
        r.TrangThai != "⏳ Đang tách..." &&
        r.TrangThai != "⏳ Đang tách/xoay...";

    private bool CanExport() =>
        !IsRunning && _runState?.CanExport == true;

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
            _errorLog.LogException(_screenKey, "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_workspace is null || !Directory.Exists(_workspace.OutputDir)) return;
        var workspace = _workspace;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        try
        {
            int copied = 0, gcnCopied = 0;
            string ocrDir = "";
            await Task.Run(() =>
            {
                // 1. Copy toàn bộ kết quả tách (GCN/GT/GTK theo thư mục) vào thư mục đã chọn.
                copied = CopyDirectory(workspace.OutputDir, destDir);
                if (_copyGcnToOcrOnExport)
                {
                    // 2. Gom tất cả file *-GCN.pdf vào thư mục cố định "OCR" BÊN TRONG thư mục đã chọn.
                    ocrDir = Path.Combine(destDir, "OCR");
                    gcnCopied = CopyGcnFiles(workspace.OutputDir, ocrDir);
                }
            }, CancellationToken.None);

            var successfulSourcePaths = Results
                .Where(record => record.TrangThai.StartsWith("Hoàn thành", StringComparison.Ordinal))
                .Select(record => record.FilePath)
                .ToList();

            StatusMessage = _copyGcnToOcrOnExport
                ? $"Đã lưu {copied} file PDF vào: {destDir}  |  {gcnCopied} file GCN → {ocrDir}"
                : $"Đã lưu {copied} file PDF vào: {destDir}";
            _folderPicker.OpenFolder(destDir);
            // Quota đã ghi nhận ngay khi StartAsync chạy xong 100% (theo số file tách ra) — không ghi lại ở đây.
            DeleteGeminiFilesInBackground(successfulSourcePaths);

            await _cache.DeleteWorkspaceAsync(workspace);
            _workspace = null;
            _runState = null;
            // Kết thúc phiên: bỏ đường dẫn + danh sách file để Start tự disable, phải chọn lại thư mục mới chạy tiếp.
            SelectedFiles.Clear();
            SelectedPath = null;
            ExportCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _errorLog.LogException(_screenKey, "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private async Task RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang)
    {
        var result = await _auth.RecordOcrCreditAsync(tenFile, duongDanFile, soTrang, CancellationToken.None);
        if (result.IsSuccess) return;

        var error = result.Error ?? "Không ghi nhận được hạn mức xử lý.";
        _errorLog.LogException(_screenKey, "StartAsync.RecordOcrCredit", new InvalidOperationException(error));
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

    /// <summary>Tên hiển thị cho quota: cắt dấu phân cách cuối trước khi lấy tên — tránh trả rỗng khi
    /// đường dẫn là gốc ổ đĩa kiểu "D:\".</summary>
    private static string GetOutputDisplayName(string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(normalized) ? path : Path.GetFileName(normalized);
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
                _errorLog.LogException(_screenKey, "ExportAsync.DeleteGeminiFiles", ex);
            }
        });
    }

    /// <summary>Copy mọi file *-GCN.pdf (đệ quy) từ thư mục tạm vào thư mục OCR (phẳng, ghi đè trùng tên).</summary>
    private static int CopyGcnFiles(string sourceDir, string ocrDir)
    {
        Directory.CreateDirectory(ocrDir);
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (!Path.GetFileNameWithoutExtension(file).EndsWith("-GCN", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(ocrDir, Path.GetFileName(file)), overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>Copy đệ quy toàn bộ file từ thư mục tạm sang đích (BỎ QUA file .json kết quả), trả về số file đã copy.</summary>
    private static int CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue; // JSON chỉ giữ trong temp
            var rel = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }
        return count;
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
