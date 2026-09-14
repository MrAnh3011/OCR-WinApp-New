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
using OCR.Business.Notifications;
using OCR.Business.Pdf;
using OCR.Business.VbdBn;
using OCR.Business.VietBdGcn;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "OCR GCN VBD-BN": quét ĐỆ QUY một thư mục gốc chứa nhiều thư mục hồ sơ, phân loại từng PDF theo
/// TÊN FILE (<see cref="VbdBnFileClassifier"/>) thành hai lô chạy CHUNG một hàng đợi upload:
///  • lô GCN — dùng lại NGUYÊN pipeline VietBD (<see cref="IVietBdGcnExtractService"/>, cache
///    <see cref="VietBdGcnRunCacheService"/>) để ra <see cref="VietBdGcnEnvelope"/>;
///  • lô GTK — pipeline CCCD riêng (<see cref="ICccdExtractService"/>) ra <see cref="CccdEnvelope"/>.
/// Lúc Export, serial GCN được gắn cho từng file GTK theo thư mục cha trực tiếp
/// (<see cref="VbdBnSerialMap"/>) rồi ghi hai sheet qua <see cref="IVbdBnExcelExporter"/>.
///
/// KHÁC BIỆT CÓ CHỦ ĐÍCH so với màn VietBD: màn này KHÔNG đổi tên file nguồn sau khi xuất — thư mục
/// nguồn phải bất biến vì serial-map dựa vào cấu trúc thư mục hồ sơ.
/// Options riêng (<see cref="VbdBnOptions"/>) và một instance cache RIÊNG (gốc <c>TempDir</c> của
/// VBD-BN) nên không dùng lẫn cache JSON với màn VietBD gốc.
/// </summary>
public partial class GcnVbdBnViewModel : ObservableObject
{
    /// <summary>Screen key riêng: error log `error-gcn-vbd-bn.log` và workspace cache tách khỏi màn VietBD.</summary>
    private const string ScreenKey = "gcn-vbd-bn";

    /// <summary>Thư mục con của workspace chứa JSON cache lô GTK — tách khỏi JSON lô GCN (khác schema).</summary>
    private const string CccdCacheFolder = "response_cccd";

    /// <summary>Màn này chỉ nhận PDF: cả hai luồng con (GCN VietBD, CCCD) đều gửi PDF trực tiếp.</summary>
    private static readonly string[] PdfExtensions = { ".pdf" };

    private readonly IFolderPickerService _folderPicker;
    private readonly IVietBdGcnExtractService _extract;
    private readonly ICccdExtractService _cccdExtract;
    private readonly IVbdBnExcelExporter _excel;
    private readonly IExportResultNotifier _notifier;
    private readonly IErrorLogService _errorLog;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly IGeminiUploadPipeline _uploadPipeline;
    private readonly IAuthService _auth;
    private readonly VietBdGcnRunCacheService _cache;
    private readonly ISplitCachePromptService _cachePrompt;
    private readonly IMaXaPromptService _maXaPrompt;
    private readonly VbdBnOptions _opt;
    private readonly IPdfRenderer _pdf;
    private readonly DispatcherQueue? _dispatcher;

    private CancellationTokenSource? _cts;
    private VietBdGcnRunWorkspace? _workspace;
    private readonly List<VietBdGcnEnvelope> _envelopes = new();
    /// <summary>Kết quả lô GTK: giữ kèm đường dẫn nguồn vì serial-map tra theo thư mục cha của file.</summary>
    private readonly List<(string GtkPath, CccdEnvelope Envelope)> _cccdEnvelopes = new();
    private readonly object _envelopesLock = new();
    private readonly Dictionary<string, VietBdGcnEnvelope> _successfulSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Loại của từng file đã chọn (key = đường dẫn tuyệt đối). Để ở ViewModel vì
    /// <see cref="SelectedFile"/> là kiểu DÙNG CHUNG của mọi màn — không nhét khái niệm riêng của màn này vào đó.</summary>
    private readonly Dictionary<string, VbdBnFileKind> _fileKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileKindsLock = new();
    private readonly HashSet<string> _failedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedSourcePathsLock = new();
    /// <summary>Mã xã người dùng nhập lúc bấm Bắt đầu — áp cho cả lô GCN, ghi vào cột B lúc Export.
    /// Giữ lại giữa các phiên để lần sau điền sẵn ô nhập.</summary>
    private string? _maXa;

    private DateTime _lastRunStartedAt = DateTime.Now;
    private DateTime _lastRunCompletedAt = DateTime.Now;

    private sealed record SuccessfulGcnSource(string SourcePath, VietBdGcnEnvelope Envelope);

    public string Title => "OCR GCN VBD-BN";
    public string Description => "Chọn thư mục gốc → OCR GCN theo luồng VietBD + trích CCCD/CMND từ file GTK vào sheet ThongTinCCCD.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportFailedSourcesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFolderCommand))]
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

    public string FileCountText
    {
        get
        {
            int gcn = 0, gtk = 0;
            foreach (var file in SelectedFiles)
            {
                switch (KindOf(file.Path))
                {
                    case VbdBnFileKind.Gcn: gcn++; break;
                    case VbdBnFileKind.Gtk: gtk++; break;
                }
            }
            return $"Tìm thấy {SelectedFiles.Count} tệp PDF ({gcn} GCN, {gtk} GTK)";
        }
    }

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<VietBdGcnRecord> Results { get; } = new();

    public GcnVbdBnViewModel(
        IFolderPickerService folderPicker,
        IVietBdGcnExtractService extract,
        ICccdExtractService cccdExtract,
        IVbdBnExcelExporter excel,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        VietBdGcnRunCacheService cache,
        ISplitCachePromptService cachePrompt,
        IMaXaPromptService maXaPrompt,
        VbdBnOptions opt,
        IPdfRenderer pdf)
    {
        _folderPicker = folderPicker;
        _extract = extract;
        _cccdExtract = cccdExtract;
        _excel = excel;
        _notifier = notifier;
        _errorLog = errorLog;
        _geminiFiles = geminiFiles;
        _uploadPipeline = uploadPipeline;
        _auth = auth;
        _cache = cache;
        _cachePrompt = cachePrompt;
        _maXaPrompt = maXaPrompt;
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

    [RelayCommand(CanExecute = nameof(CanPickFolder))]
    private async Task PickFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (path is null) return;
        try
        {
            var workspace = _cache.GetWorkspace(ScreenKey, path);
            ResetSelection();
            AddFolder(path);
            SelectedPath = path;
            _workspace = workspace;
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    /// <summary>Quét đệ quy PDF của thư mục gốc, giữ lại file phân loại được (GCN/GTK), bỏ qua phần còn lại.</summary>
    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in _folderPicker.EnumerateFiles(folder, PdfExtensions, recursive: true))
        {
            var kind = VbdBnFileClassifier.Classify(
                Path.GetFileNameWithoutExtension(file), _opt.GcnKeyword, _opt.GtkKeyword);
            if (kind == VbdBnFileKind.None) continue;
            if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;

            lock (_fileKindsLock)
            {
                _fileKinds[file] = kind;
            }
            // Name = đường dẫn tương đối từ thư mục đã chọn (quét đệ quy cả thư mục con).
            SelectedFiles.Add(new SelectedFile(Path.GetRelativePath(folder, file), file));
        }
    }

    private VbdBnFileKind KindOf(string path)
    {
        lock (_fileKindsLock)
        {
            return _fileKinds.TryGetValue(path, out var kind) ? kind : VbdBnFileKind.None;
        }
    }

    private void ResetSelection()
    {
        SelectedFiles.Clear();
        Results.Clear();
        lock (_fileKindsLock)
        {
            _fileKinds.Clear();
        }
        lock (_envelopesLock)
        {
            _envelopes.Clear();
            _cccdEnvelopes.Clear();
            _successfulSources.Clear();
        }
        ClearFailedSourcePaths();
        ProgressValue = 0;
        StatusMessage = null;
        ExportCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsRunning && SelectedFiles.Count > 0;

    private bool CanPickFolder => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedFiles.Count == 0 || _workspace is null) return;

        var useCachedJson = false;
        try
        {
            await _geminiFiles.PrepareExecutionAsync();
            if (HasJsonCacheForSelection(_workspace, SelectedFiles.Select(f => f.Path)))
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
            _errorLog.LogException(ScreenKey, "StartAsync.PrepareCache", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
            return;
        }

        // Hỏi mã xã SAU hộp thoại cache (theo thứ tự chủ dự án chốt) và TRƯỚC khi khoá UI chạy lô:
        // giấy chứng nhận không in mã xã của thửa đất nên cột B chỉ có thể lấy từ người dùng.
        // Bấm Hủy / bỏ trống = không chạy, giữ nguyên trạng thái để bấm Bắt đầu lại.
        // Mã xã CHỈ áp cho lô GCN: lần chạy không có file GCN nào thì không hỏi (sheet CCCD không dùng mã xã).
        if (SelectedFiles.Any(f => KindOf(f.Path) == VbdBnFileKind.Gcn))
        {
            var maXa = await _maXaPrompt.AskAsync(_maXa);
            if (string.IsNullOrWhiteSpace(maXa)) return;
            _maXa = maXa.Trim();
        }

        ErrorMessage = null;
        Results.Clear();
        lock (_envelopesLock)
        {
            _envelopes.Clear();
            _cccdEnvelopes.Clear();
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
        var stubMap = new Dictionary<string, VietBdGcnRecord>();
        foreach (var sf in selected)
        {
            var stub = new VietBdGcnRecord { FilePath = sf.Path, FileName = sf.Name, TrangThai = "Chờ xử lý" };
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
            // Cache JSON của hai lô nằm hai nơi khác nhau -> phải hỏi đúng theo loại file,
            // nếu không file GTK trúng cache vẫn bị upload lại (và ngược lại).
            WillHitJsonCache = path => useCachedJson && HasFreshJson(workspace, path),
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

                        if (KindOf(item.SourcePath) == VbdBnFileKind.Gtk)
                        {
                            var cccdEnvelope = await _cccdExtract.ProcessFileAsync(
                                item.SourcePath, null, ct,
                                GetCccdJsonPath(workspace, item.SourcePath), useCachedJson, item.Files);
                            if (cccdEnvelope == null) throw new Exception("Không nhận được dữ liệu phù hợp từ file.");

                            lock (_envelopesLock)
                            {
                                _cccdEnvelopes.Add((item.SourcePath, cccdEnvelope));
                            }
                            // File GTK không sinh dòng thửa: giữ nguyên dòng lưới, chỉ đổi trạng thái.
                            // File 0 giấy tờ vẫn là THÀNH CÔNG (spec §6) — không phải lỗi đọc.
                            var count = cccdEnvelope.danh_sach_giay_to.Count;
                            Ui(() => stub.TrangThai = $"✅ {count} CCCD/CMND");
                            ok = true;
                        }
                        else
                        {
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
                                // đọc sau đó, không để nó tiếp tục mang chuỗi "⏳ Đang xử lý..." dù đã xong.
                                stub.TrangThai = "Thành công";
                                Results.Remove(stub);
                                AddEnvelopeRows(item.SourcePath, envelope);
                            });
                            ok = true;
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            _errorLog.LogException(ScreenKey, "StartAsync.ProcessFileCanceled:" + Path.GetFileName(item.SourcePath), ex);
                            AddFailedSourcePath(item.SourcePath);
                        }
                        Ui(() => stub.TrangThai = ct.IsCancellationRequested
                            ? "Đã dừng"
                            : "❌ " + UserFacingError.ProcessFile(ex));
                    }
                    catch (Exception ex)
                    {
                        _errorLog.LogException(ScreenKey, "StartAsync.ProcessFile:" + Path.GetFileName(item.SourcePath), ex);
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
                _errorLog.LogException(ScreenKey, "StartAsync.ConsumerLoop", ex);
            }
        })).ToList();

        await Task.WhenAll(consumers);

        // Chốt phiên trong finally: nếu bước tổng kết/báo cáo có lỗi thì IsRunning vẫn phải về false,
        // nếu không cả Start lẫn Export đều bị khóa vĩnh viễn cho tới khi khởi động lại app.
        try
        {
            // Chờ producer dừng hẳn TRƯỚC khi tổng kết: nếu không, người dùng bấm Dừng có thể khiến
            // producer chốt muộn rồi ghi đè ngược tiến độ/trạng thái đã hiển thị xong.
            await queue.Completion;

            // Bấm Dừng giữa chừng có thể để lại vài nguồn đã publish xong nhưng chưa consumer nào kịp
            // claim -> chốt nốt để "done" đủ tổng số file. SettleRemaining trả về ĐÚNG danh sách nguồn
            // vừa chốt — đây là nguồn sự thật duy nhất để biết nguồn nào "không ai xử lý", TUYỆT ĐỐI
            // không được suy đoán qua chuỗi TrangThai hiển thị.
            var abandonedSources = queue.SettleRemaining(false, "Đã dừng");

            // Ép ProgressValue = 100 NGAY sau khi mọi nguồn đã chốt xong, TRƯỚC hai vòng quét lưới bên
            // dưới: một property setter bị bind lỡ ném lỗi sẽ nhảy thẳng ra catch và bỏ qua dòng này —
            // khiến CanExport (đòi ProgressValue >= 100) khoá Export vĩnh viễn cho phiên đó.
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
                    _errorLog.LogException(ScreenKey, "StartAsync.UploadFailed:" + Path.GetFileName(failure.SourcePath),
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
                    _errorLog.LogException(ScreenKey, "StartAsync.StuckItem:" + Path.GetFileName(sourcePath),
                        new InvalidOperationException("File còn dang dở sau khi luồng xử lý đã kết thúc."));
                    AddFailedSourcePath(sourcePath);
                    Ui(() => stub.TrangThai = "❌ " + UserFacingError.ProcessFileFallback);
                }
            }

            _lastRunCompletedAt = DateTime.Now;
            int envelopeCount, gtkFileCount, cccdDocCount;
            List<VietBdGcnEnvelope> reportSnapshot;
            lock (_envelopesLock)
            {
                envelopeCount = _envelopes.Count;
                gtkFileCount = _cccdEnvelopes.Count;
                cccdDocCount = _cccdEnvelopes.Sum(x => x.Envelope.danh_sach_giay_to.Count);
                reportSnapshot = _envelopes.ToList();
            }

            var gtkSummary = $"GTK: {gtkFileCount} file / {cccdDocCount} giấy tờ";
            StatusMessage = envelopeCount > 0 || gtkFileCount > 0
                ? $"Hoàn tất: {success} thành công, {failed} lỗi, {gtkSummary}. Bấm Export để xuất Excel."
                : $"Hoàn tất: {success} thành công, {failed} lỗi, {gtkSummary}. Không có dữ liệu để xuất.";

            var rowCount = reportSnapshot.Sum(CountDisplayRows);
            _notifier.NotifyExport(
                Title,
                "Hoàn tất luồng OCR GCN VBD-BN",
                _lastRunStartedAt,
                _lastRunCompletedAt,
                rowCount,
                SelectedPath ?? "",
                new[]
                {
                    new ExportReportItem { Name = "Số file đã chọn", Value = files.Count.ToString() },
                    new ExportReportItem { Name = "Số GCN đọc thành công", Value = envelopeCount.ToString() },
                    new ExportReportItem { Name = "Số file GTK đọc thành công", Value = gtkFileCount.ToString() },
                    new ExportReportItem { Name = "Số giấy tờ CCCD/CMND đọc được", Value = cccdDocCount.ToString() },
                    new ExportReportItem { Name = "Số file lỗi", Value = failed.ToString() },
                    new ExportReportItem { Name = "Số dòng dữ liệu đọc được", Value = rowCount.ToString() },
                    new ExportReportItem { Name = "Kết quả", Value = StatusMessage ?? "" }
                });
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "StartAsync.Finalize", ex);
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

    /// <summary>
    /// Đường dẫn JSON cache của một file GTK: giữ nguyên đường dẫn TƯƠNG ĐỐI trong thư mục con
    /// <c>response_cccd</c> của workspace. KHÔNG rút gọn còn mỗi tên file: thư mục hồ sơ nào cũng có
    /// thể chứa một file tên "GTK.pdf", gộp theo tên sẽ khiến hồ sơ này đọc nhầm cache của hồ sơ khác.
    /// </summary>
    private static string GetCccdJsonPath(VietBdGcnRunWorkspace workspace, string filePath)
    {
        var relative = Path.GetRelativePath(workspace.SourceFolder, Path.GetFullPath(filePath));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("File không nằm trong thư mục nguồn OCR GCN VBD-BN.");

        return Path.Combine(workspace.CacheDir, CccdCacheFolder, Path.ChangeExtension(relative, ".json"));
    }

    /// <summary>Cache JSON tương ứng LOẠI file — GCN đi đường của cache service, GTK đi thư mục CCCD.</summary>
    private string GetJsonPathForKind(VietBdGcnRunWorkspace workspace, string filePath) =>
        KindOf(filePath) == VbdBnFileKind.Gtk
            ? GetCccdJsonPath(workspace, filePath)
            : _cache.GetJsonPath(workspace, filePath);

    /// <summary>Có ít nhất một file trong lô đã có JSON cache (dù cũ) → mới hỏi hộp thoại cache.</summary>
    private bool HasJsonCacheForSelection(VietBdGcnRunWorkspace workspace, IEnumerable<string> sourceFiles)
    {
        foreach (var file in sourceFiles)
        {
            try
            {
                if (File.Exists(file) && File.Exists(GetJsonPathForKind(workspace, file))) return true;
            }
            catch
            {
                // Đường dẫn cache dựng không được (file ngoài thư mục nguồn) = coi như không có cache.
            }
        }
        return false;
    }

    /// <summary>File này có JSON cache còn mới hơn file nguồn không — dùng để bỏ qua upload.</summary>
    private bool HasFreshJson(VietBdGcnRunWorkspace workspace, string sourcePath)
    {
        if (KindOf(sourcePath) != VbdBnFileKind.Gtk) return _cache.HasFreshJson(workspace, sourcePath);

        try
        {
            var jsonPath = GetCccdJsonPath(workspace, sourcePath);
            if (!File.Exists(jsonPath) || !File.Exists(sourcePath)) return false;
            return File.GetLastWriteTimeUtc(jsonPath) >= File.GetLastWriteTimeUtc(sourcePath);
        }
        catch { return false; }
    }

    /// <summary>Mỗi thửa (danh_sach_dong) → 1 dòng lưới. Việc nở dòng theo MĐSD chỉ xảy ra lúc ghi Excel.</summary>
    private void AddEnvelopeRows(string file, VietBdGcnEnvelope envelope)
    {
        // Hiển thị đường dẫn tương đối từ thư mục đã chọn (đã tính sẵn trong SelectedFiles).
        var displayName = SelectedFiles.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))?.Name
                          ?? Path.GetFileName(file);
        var info = envelope.thong_tin_gcn;
        var chu = string.Join("; ", (info.chu_su_dung_chi_tiet ?? new()).Select(c => c.ho_ten ?? ""));
        var rows = envelope.danh_sach_dong ?? new();
        if (rows.Count == 0)
        {
            Results.Add(new VietBdGcnRecord
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
            Results.Add(new VietBdGcnRecord
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

        // Dùng chung bảng ánh xạ MIME với NewGcnExtractService để không lệch nhãn khi upload (đặc biệt TIFF).
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

    /// <summary>Số thửa đọc được của một GCN (GCN không có dòng thửa nào vẫn tính 1 — có một dòng fallback cần rà soát).</summary>
    private static int CountDisplayRows(VietBdGcnEnvelope envelope)
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
            // Lô chỉ có file GTK vẫn xuất được: sheet ThongTinCCCD có dữ liệu, sheet KeKhaiDangKy rỗng.
            return !IsRunning && ProgressValue >= 100 && (_envelopes.Count > 0 || _cccdEnvelopes.Count > 0);
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
            _errorLog.LogException(ScreenKey, "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        List<VietBdGcnEnvelope> snapshot;
        List<SuccessfulGcnSource> successfulSources;
        List<(string GtkPath, CccdEnvelope Envelope)> cccdSnapshot;
        lock (_envelopesLock)
        {
            snapshot = _envelopes.ToList();
            successfulSources = _successfulSources
                .Select(kv => new SuccessfulGcnSource(kv.Key, kv.Value))
                .ToList();
            cccdSnapshot = _cccdEnvelopes.ToList();
        }
        if (snapshot.Count == 0 && cccdSnapshot.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outPath = Path.Combine(destDir, $"GCN-VBD-BN-output_{stamp}.xlsx");

            // Serial của GTK lấy từ (các) GCN OCR thành công CÙNG THƯ MỤC CHA — tra theo đường dẫn
            // nguồn thật, đó là lý do màn này không đổi tên/di chuyển file nguồn sau khi xuất.
            var map = VbdBnSerialMap.Build(successfulSources
                .Select(s => (s.SourcePath, s.Envelope.thong_tin_gcn.so_serial)));
            var cccdRows = new List<CccdExportRow>();
            foreach (var (gtkPath, env) in cccdSnapshot)
            {
                var (serial, warning) = VbdBnSerialMap.Resolve(gtkPath, map);
                foreach (var record in env.danh_sach_giay_to)
                    cccdRows.Add(new CccdExportRow(serial, record, Path.GetFileName(gtkPath),
                        warning is null ? Array.Empty<string>() : new[] { warning }));
            }

            var (gcnRows, cccdRowCount) = await Task.Run(
                () => _excel.Write(snapshot, cccdRows, outPath, _opt.TemplateExcel, _maXa),
                CancellationToken.None);

            // Quota lô GCN trừ theo SỐ THỬA, KHÔNG theo số dòng Excel: khuôn Việt Bản Đồ nở dòng theo
            // (mục đích sử dụng × chủ đồng sử dụng) nên đếm theo dòng sẽ trừ gấp nhiều lần màn iLIS
            // cho cùng một lượng giấy tờ đã xử lý.
            var parcelCount = snapshot.Sum(CountDisplayRows);
            await RecordOcrCreditAsync(Path.GetFileName(outPath), outPath, parcelCount);

            // Lô GTK trừ theo TỪNG FILE, soTrang = số giấy tờ đọc được; file không có giấy tờ nào
            // KHÔNG gọi ghi nhận (spec §6) — không trừ hạn mức cho kết quả rỗng.
            foreach (var (gtkPath, env) in cccdSnapshot)
            {
                if (env.danh_sach_giay_to.Count > 0)
                    await RecordOcrCreditAsync(Path.GetFileName(gtkPath), gtkPath, env.danh_sach_giay_to.Count);
            }

            StatusMessage = $"Đã xuất {gcnRows} dòng GCN + {cccdRowCount} dòng CCCD/CMND → {outPath}";
            _folderPicker.OpenFolder(destDir);
            DeleteGeminiFilesInBackground(
                successfulSources.Select(x => x.SourcePath).Concat(cccdSnapshot.Select(x => x.GtkPath)));
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private async Task RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang)
    {
        var result = await _auth.RecordOcrCreditAsync(tenFile, duongDanFile, soTrang, CancellationToken.None);
        if (result.IsSuccess) return;

        var error = result.Error ?? "Không ghi nhận được hạn mức xử lý.";
        _errorLog.LogException(ScreenKey, "ExportAsync.RecordOcrCredit", new InvalidOperationException(error));
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
                _errorLog.LogException(ScreenKey, "ExportAsync.DeleteGeminiFiles", ex);
            }
        });
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
