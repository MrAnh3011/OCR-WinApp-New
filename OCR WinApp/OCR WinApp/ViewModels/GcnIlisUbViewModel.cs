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
using OCR.Business.IlisUb;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.Notifications;
using OCR.Business.Pdf;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "OCR GCN iLis-UB": quét đệ quy một thư mục cha, CHỈ nhận file PDF có cụm "GCN" trong tên,
/// OCR bằng đúng pipeline của màn iLIS, rồi Export ra một CÂY THƯ MỤC MỚI đã đổi tên theo số serial
/// và gộp file kèm theo thành {nhãn}-GT.pdf / {nhãn}-GTK.pdf theo file quy tắc JSON.
///
/// Khác màn iLIS đúng ba chỗ: bộ lọc file đầu vào, screen key, và nhánh Export (KHÔNG đổi tên file nguồn).
/// Prompt/schema/extract service/exporter Excel/cache đều DÙNG CHUNG với màn iLIS.
/// </summary>
public partial class GcnIlisUbViewModel : ObservableObject
{
    /// <summary>Screen key cho error log + workspace cache. KHÔNG đổi để cache/log trên máy người dùng không mồ côi.</summary>
    private const string ScreenKey = "gcn-ilis-ub";

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
    private readonly GcnIlisUbOptions _opt;
    private readonly IPdfRenderer _pdf;
    private readonly IGcnFolderRulesLoader _rulesLoader;
    private readonly IGcnTreeExporter _treeExporter;
    private GcnFolderRules? _rules;
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

    private sealed record SuccessfulGcnSource(string SourcePath, NewGcnEnvelope Envelope);

    public string Title => "OCR GCN iLis-UB";
    public string Description => "Chọn thư mục cha chứa hồ sơ GCN → quét đệ quy, chỉ OCR file PDF có cụm \"GCN\" trong tên. Sau khi hoàn tất, bấm Export để xuất Excel và dựng cây thư mục đã đổi tên theo số serial.";

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
    public string FileCountText => $"Tìm thấy {SelectedFiles.Count} tệp PDF có cụm \"GCN\" trong tên";

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<NewGcnRecord> Results { get; } = new();

    public GcnIlisUbViewModel(
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
        GcnIlisUbOptions opt,
        IPdfRenderer pdf,
        IGcnFolderRulesLoader rulesLoader,
        IGcnTreeExporter treeExporter)
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
        _rulesLoader = rulesLoader;
        _treeExporter = treeExporter;
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
            // Nạp quy tắc NGAY ở bước chọn thư mục: GcnKeyword quyết định file nào được quét, và
            // nếu file JSON hỏng thì báo ngay chứ không để người dùng OCR xong mới biết không Export được.
            _rules = _rulesLoader.Load(_opt.RulesFile);
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

    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder) || _rules is null) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (!IsAcceptedSourceFile(file, _rules)) continue;
            if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
            // Name = đường dẫn tương đối từ thư mục đã chọn (quét đệ quy cả thư mục con).
            SelectedFiles.Add(new SelectedFile(Path.GetRelativePath(folder, file), file));
        }
    }

    /// <summary>
    /// Bộ lọc đầu vào của màn này: CHỈ file .pdf và tên file (không kể đuôi) phải chứa cụm GcnKeyword.
    /// Ảnh bị loại kể cả khi tên có "GCN" — hồ sơ Uông Bí luôn là PDF, nhận ảnh chỉ làm bẩn cây đích.
    /// Để internal static để test harness gọi được mà không phải dựng cả ViewModel.
    /// </summary>
    internal static bool IsAcceptedSourceFile(string path, GcnFolderRules rules)
    {
        if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        return rules.IsGcnFileName(Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Thư mục lưu kết quả có nằm TRONG (hoặc chính là) thư mục nguồn không.
    ///
    /// Phải chặn: màn này quét đệ quy nên nếu ghi cây kết quả vào giữa thư mục nguồn thì lần quét sau
    /// nuốt luôn các file "{nhãn}-GCN.pdf" vừa sinh làm nguồn OCR mới — nhân đôi dữ liệu và tốn quota.
    /// Để internal static để test harness gọi được mà không phải dựng cả ViewModel (giống
    /// <see cref="IsAcceptedSourceFile"/>).
    /// </summary>
    internal static bool IsDestinationInsideSource(string? sourceRoot, string? destination)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destination)) return false;

        try
        {
            // Chuẩn hoá dấu phân cách CUỐI cho cả hai vế rồi so tiền tố: có dấu phân cách cuối thì
            // "E:\HoSo2" không bị nhận nhầm là nằm trong "E:\HoSo"; phép so tiền tố cũng phủ luôn
            // trường hợp hai đường dẫn TRÙNG NHAU.
            var src = WithTrailingSeparator(sourceRoot);
            var dest = WithTrailingSeparator(destination);
            return dest.StartsWith(src, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Đường dẫn không chuẩn hoá được thì để các bước ghi file bên dưới báo lỗi thật, không
            // chặn nhầm người dùng ở đây.
            return false;
        }
    }

    private static string WithTrailingSeparator(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
           + Path.DirectorySeparatorChar;

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
            _errorLog.LogException(ScreenKey, "StartAsync.PrepareCache", ex);
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
                "Hoàn tất luồng OCR GCN iLis-UB",
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

        // CÙNG phép kiểm với ExportAsync: file lỗi vẫn là file GCN (tên chứa "GCN"), rải chúng vào giữa
        // thư mục nguồn thì lần quét sau nhận luôn làm nguồn OCR mới — đúng vòng lặp nhân đôi dữ liệu.
        // Nay còn copy cả thư mục hồ sơ nên phạm vi lây lan càng rộng, phép chặn này càng cần thiết.
        // SelectedPath rỗng/null thì IsDestinationInsideSource trả false, luồng đi tiếp bình thường.
        if (IsDestinationInsideSource(SelectedPath, destDir))
        {
            ErrorMessage =
                $"Thư mục lưu file lỗi (\"{destDir}\") đang nằm trong hoặc chính là thư mục nguồn (\"{SelectedPath}\"). " +
                "Hãy chọn một thư mục nằm NGOÀI thư mục nguồn: nút này copy cả thư mục hồ sơ (gồm file GCN) " +
                "nên nếu ghi vào giữa dữ liệu gốc, lần quét sau sẽ nhận chúng làm file đầu vào, gây nhân đôi " +
                "dữ liệu và tốn hạn mức xử lý.";
            return;
        }

        List<string> successfulPaths;
        lock (_envelopesLock)
        {
            successfulPaths = _successfulSources.Keys.ToList();
        }

        try
        {
            // Gom CẢ THƯ MỤC HỒ SƠ chứa GCN lỗi, không chỉ mỗi file GCN — chạy lại là có đủ GT/GTK/ảnh.
            // Loại trừ những gì đã OCR xong để không xử lý lại và không tốn thêm hạn mức.
            var collected = await Task.Run(
                () => FailedHoSoFileCollector.Collect(failedPaths, successfulPaths),
                CancellationToken.None);

            var copied = await Task.Run(
                () => FailedSourceFileExporter.CopyFiles(collected.Files, destDir, SelectedPath),
                CancellationToken.None);

            foreach (var warning in collected.Warnings)
            {
                _errorLog.LogException(ScreenKey, "ExportFailedSourcesAsync.Collect",
                    new InvalidOperationException(warning));
            }

            StatusMessage =
                $"Đã xuất {copied} file từ {collected.HoSoFolders} thư mục hồ sơ lỗi vào: {destDir}" +
                (collected.Warnings.Count > 0 ? $" ({collected.Warnings.Count} thư mục không đọc được)" : "");
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

        var sourceRoot = SelectedPath;
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            ErrorMessage = "Chưa xác định được thư mục nguồn để dựng cây kết quả.";
            return;
        }

        // TOÀN BỘ file đã quét (kể cả file OCR lỗi) — dùng để loại file GCN khỏi tập "file kèm theo"
        // theo ĐƯỜNG DẪN, không phụ thuộc GcnKeyword hiện hành (xem GcnTreeExportRequest.ScannedGcnPaths).
        var scannedGcnPaths = SelectedFiles.Select(f => f.Path).ToList();

        // Nạp LẠI quy tắc mỗi lần Export: chủ dự án sửa file JSON rồi Export lại là ăn ngay,
        // không phải chọn lại thư mục hay khởi động lại app.
        GcnFolderRules rules;
        try
        {
            rules = _rulesLoader.Load(_opt.RulesFile);
            _rules = rules;
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportAsync.LoadRules", ex);
            ErrorMessage = ex.Message;
            return;
        }

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        // Chặn TRƯỚC khi ghi bất cứ thứ gì: xem chú thích của IsDestinationInsideSource.
        if (IsDestinationInsideSource(sourceRoot, destDir))
        {
            ErrorMessage =
                $"Thư mục lưu kết quả (\"{destDir}\") đang nằm trong hoặc chính là thư mục nguồn (\"{sourceRoot}\"). " +
                "Hãy chọn một thư mục nằm NGOÀI thư mục nguồn: nếu không, cây kết quả sẽ bị ghi lẫn vào dữ liệu gốc " +
                "và các file \"{nhãn}-GCN.pdf\" vừa sinh sẽ bị lần quét sau nhận nhầm làm file đầu vào, " +
                "gây nhân đôi dữ liệu và tốn hạn mức xử lý.";
            return;
        }

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outPath = Path.Combine(destDir, $"GCN-iLisUB-output_{stamp}.xlsx");
            var rowCount = await Task.Run(
                () => _excel.Write(snapshot, outPath, _opt.TemplateExcel),
                CancellationToken.None);

            var treeRequest = new GcnTreeExportRequest(
                sourceRoot,
                destDir,
                successfulSources
                    .Select(s => new GcnTreeSource(s.SourcePath, s.Envelope.thong_tin_gcn.so_serial))
                    .ToList(),
                rules,
                scannedGcnPaths);
            var tree = await Task.Run(() => _treeExporter.Export(treeRequest), CancellationToken.None);

            foreach (var warning in tree.Warnings)
                _errorLog.LogException(ScreenKey, "ExportAsync.TreeWarning", new InvalidOperationException(warning));

            await RecordOcrCreditAsync(Path.GetFileName(outPath), outPath, rowCount);
            StatusMessage =
                $"Đã xuất {rowCount} dòng dữ liệu từ {snapshot.Count} GCN → {outPath}. " +
                $"Cây thư mục: {tree.LabelFolders} thư mục serial từ {tree.HoSoFolders} hồ sơ, " +
                $"{tree.MergedFiles} file đã gộp, {tree.CopiedAsIsFiles} file giữ nguyên tên, " +
                $"{tree.MissingSerialFolders} hồ sơ thiếu serial, {tree.Warnings.Count} cảnh báo → {tree.RootPath}";
            _folderPicker.OpenFolder(destDir);
            DeleteGeminiFilesInBackground(successfulSources.Select(x => x.SourcePath));
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
