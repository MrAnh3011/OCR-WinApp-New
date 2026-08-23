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
using OCR.Business.UyBan;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "OCR Đất Uỷ Ban" (Mẫu số 15): chọn thư mục PDF → cắt PDF → API vision → lưới + Excel.
/// Điều phối chạy song song, cập nhật real-time (port từ process_and_ocr_api.py).
/// </summary>
public partial class DatUyBanViewModel : ObservableObject
{
    private readonly IFolderPickerService _folderPicker;
    private readonly IUyBanSplitService _split;
    private readonly IUyBanExtractService _extract;
    private readonly IUyBanExcelExporter _excel;
    private readonly IExportResultNotifier _notifier;
    private readonly IErrorLogService _errorLog;
    private readonly IGeminiFileApiService _geminiFiles;
    private readonly IGeminiUploadPipeline _uploadPipeline;
    private readonly IAuthService _auth;
    private readonly UyBanOptions _opt;
    private readonly IPdfRenderer _pdf;
    private readonly DispatcherQueue? _dispatcher;

    private CancellationTokenSource? _cts;
    private DateTime _lastRunStartedAt = DateTime.Now;
    private DateTime _lastRunCompletedAt = DateTime.Now;
    private readonly HashSet<string> _successfulSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _successfulSourcePathsLock = new();
    private readonly HashSet<string> _failedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedSourcePathsLock = new();

    public string Title => "OCR Đất Uỷ Ban";
    public string Description => "Chọn thư mục chứa PDF nhiều đơn → tách mỗi đơn 2 trang, OCR trang 1, đổi tên PDF con theo số thửa/tờ, rồi Export Excel + PDF con.";

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
    public string FileCountText => $"Tìm thấy {SelectedFiles.Count} tệp PDF";

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<UyBanRecord> Results { get; } = new();

    public DatUyBanViewModel(
        IFolderPickerService folderPicker,
        IUyBanSplitService split,
        IUyBanExtractService extract,
        IUyBanExcelExporter excel,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        UyBanOptions opt,
        IPdfRenderer pdf)
    {
        _folderPicker = folderPicker;
        _split = split;
        _extract = extract;
        _excel = excel;
        _notifier = notifier;
        _errorLog = errorLog;
        _geminiFiles = geminiFiles;
        _uploadPipeline = uploadPipeline;
        _auth = auth;
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
        ResetSelection();
        AddFolder(path);
        SelectedPath = path;
    }

    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
        {
            if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
            // Name = đường dẫn tương đối từ thư mục đã chọn (quét đệ quy cả thư mục con).
            SelectedFiles.Add(new SelectedFile(Path.GetRelativePath(folder, file), file));
        }
    }

    private void ResetSelection()
    {
        SelectedFiles.Clear();
        Results.Clear();
        ProgressValue = 0;
        ClearFailedSourcePaths();
        StatusMessage = null;
    }

    private bool CanStart() => !IsRunning && SelectedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedFiles.Count == 0) return;

        try
        {
            await _geminiFiles.PrepareExecutionAsync();
        }
        catch (Exception ex)
        {
            _errorLog.LogException("dat-uy-ban", "StartAsync.PrepareGeminiFiles", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
            return;
        }

        ErrorMessage = null;
        Results.Clear();
        lock (_successfulSourcePathsLock)
        {
            _successfulSourcePaths.Clear();
        }
        ClearFailedSourcePaths();
        IsRunning = true;
        ProgressValue = 0;
        _lastRunStartedAt = DateTime.Now;
        _lastRunCompletedAt = _lastRunStartedAt;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Dọn & tạo lại thư mục tạm cho phiên này.
        try { if (Directory.Exists(_opt.TempDir)) Directory.Delete(_opt.TempDir, recursive: true); }
        catch (Exception ex) { _errorLog.LogException("dat-uy-ban", "StartAsync.ResetTemp", ex); }
        Directory.CreateDirectory(_opt.TempDir);

        var sources = SelectedFiles.Select(f => f.Path).ToList();

        // PHA 1: tách mỗi PDF nguồn → các PDF con 2 trang (ghi vào thư mục tạm).
        Ui(() => StatusMessage = "Đang tách PDF thành các đơn 2 trang...");
        List<string> children;
        List<string> splitFailed;
        // Ghi nhớ thư mục con (tương đối từ thư mục đã chọn) của file nguồn cho từng PDF con để hiển thị trên lưới.
        var childRelDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var childSourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            (children, splitFailed) = await Task.Run(() => SplitAllSources(sources, childRelDirs, childSourcePaths, ct), ct);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã dừng khi đang tách PDF.";
            IsRunning = false; _cts?.Dispose(); _cts = null; return;
        }
        catch (Exception ex)
        {
            _errorLog.LogException("dat-uy-ban", "StartAsync.SplitAllSources", ex);
            ErrorMessage = UserFacingError.SplitPdf(ex);
            IsRunning = false; _cts?.Dispose(); _cts = null; return;
        }

        if (children.Count == 0)
        {
            foreach (var failedSource in splitFailed)
            {
                AddFailedSourcePath(failedSource);
            }
            StatusMessage = $"Không tách được đơn nào (lỗi {splitFailed.Count} file). Kiểm tra lại PDF nguồn.";
            IsRunning = false;
            ExportFailedSourcesCommand.NotifyCanExecuteChanged();
            _cts?.Dispose();
            _cts = null;
            return;
        }

        foreach (var failedSource in splitFailed)
        {
            AddFailedSourcePath(failedSource);
        }

        // Mỗi PDF con (1 đơn) = 1 dòng lưới; tên hiển thị kèm thư mục con của file nguồn.
        var recordMap = new Dictionary<string, UyBanRecord>();
        foreach (var c in children)
        {
            var stub = new UyBanRecord { FilePath = c, FileName = DisplayName(childRelDirs, c), TrangThai = "Chờ xử lý" };
            Results.Add(stub);
            recordMap[c] = stub;
        }

        // PHA 2: OCR trang 1 mỗi PDF con (qua hàng đợi upload dùng chung) + đổi tên theo {thửa}_{tờ}.
        int workers = Math.Max(1, _opt.Workers);
        int done = 0, success = 0, failed = 0;
        var startTime = DateTime.Now;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // chống trùng tên PDF con trong thư mục tạm

        // Chốt sổ tại ĐÚNG MỘT chỗ: mọi thay đổi tiến độ đi qua đây.
        // Truyền qua OnItemSettled trong request — KHÔNG tự "+=" vào queue.ItemSettled sau khi
        // Start trả về: producer chạy NGAY bên trong Start, nguồn nào chốt trước khi kịp "+=" sẽ
        // làm mất sự kiện, "done" không bao giờ đủ tổng số đơn và ProgressValue kẹt dưới 100.
        var queue = _uploadPipeline.Start(new GeminiUploadRequest
        {
            SourcePaths = children,
            PrepareArtifactsAsync = async (childPath, token) => new[]
            {
                new UploadArtifact(
                    "page-0001",
                    Path.GetFileNameWithoutExtension(childPath) + "-page-0001.jpg",
                    "image/jpeg",
                    await _pdf.RenderPageJpegAsync(childPath, 0, token))
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
                int d = Interlocked.Increment(ref done);

                Ui(() =>
                {
                    ProgressValue = (double)d / children.Count * 100;
                    StatusMessage = $"Đang OCR: {d}/{children.Count}  |  ✅ {success}  ❌ {failed}{Eta(startTime, d, children.Count)}";
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
                    try
                    {
                        Ui(() => rec.TrangThai = "⏳ Đang xử lý...");
                        var result = await _extract.ExtractAsync(item.SourcePath, ct, item.Files);
                        var renamed = TryRenameChild(item.SourcePath, result.ThuaDatSo, result.ToBanDoSo, usedNames);
                        lock (_successfulSourcePathsLock)
                        {
                            _successfulSourcePaths.Add(item.SourcePath);
                        }
                        Ui(() =>
                        {
                            rec.UpdateFrom(result);
                            rec.FilePath = renamed;
                            rec.FileName = DisplayName(childRelDirs, renamed, item.SourcePath);
                            rec.TrangThai = "Hoàn thành";
                        });
                        ok = true;
                    }
                    catch (OperationCanceledException ex)
                    {
                        // Timeout của AI client cũng ném OperationCanceledException dù token màn hình CHƯA cancel.
                        if (!ct.IsCancellationRequested)
                        {
                            _errorLog.LogException("dat-uy-ban", "StartAsync.OcrCanceled:" + Path.GetFileName(item.SourcePath), ex);
                            AddFailedSourcePath(GetOriginalSourcePath(childSourcePaths, item.SourcePath));
                        }

                        Ui(() =>
                        {
                            if (ct.IsCancellationRequested)
                            {
                                rec.TrangThai = "Đã dừng";
                            }
                            else
                            {
                                var message = UserFacingError.ProcessFile(ex);
                                rec.ErrorMessage = message;
                                rec.TrangThai = "❌ " + message;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _errorLog.LogException("dat-uy-ban", "StartAsync.Ocr:" + Path.GetFileName(item.SourcePath), ex);
                        AddFailedSourcePath(GetOriginalSourcePath(childSourcePaths, item.SourcePath));
                        Ui(() =>
                        {
                            var message = UserFacingError.ProcessFile(ex);
                            rec.ErrorMessage = message;
                            rec.TrangThai = "❌ " + message;
                        });
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
                _errorLog.LogException("dat-uy-ban", "StartAsync.ConsumerLoop", ex);
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

            // Bấm Dừng giữa chừng có thể để lại vài đơn đã publish xong nhưng chưa consumer nào kịp
            // claim (ClaimNextAsync hủy ngay dù còn việc đọc được) -> chốt nốt để "done" đủ tổng số
            // đơn. SettleRemaining trả về ĐÚNG danh sách nguồn vừa chốt — đây là nguồn sự thật duy
            // nhất để biết đơn nào "không ai xử lý". TUYỆT ĐỐI không được suy đoán qua chuỗi TrangThai
            // hiển thị.
            var abandonedSources = queue.SettleRemaining(false, "Đã dừng");

            // Ép ProgressValue = 100 NGAY sau khi mọi nguồn đã chốt xong (Completion + SettleRemaining ở
            // trên), TRƯỚC hai vòng quét cập nhật lưới bên dưới: Ui(...) chạy đồng bộ khi gọi từ UI thread,
            // một property setter bị bind lỡ ném lỗi trong hai vòng đó sẽ nhảy thẳng ra catch bên dưới và bỏ
            // qua dòng này — khiến CanExport (đòi ProgressValue >= 100) khoá Export vĩnh viễn cho phiên đó.
            if (children.Count > 0)
            {
                ProgressValue = 100;
            }

            _lastRunCompletedAt = DateTime.Now;

            // File hỏng ở phía upload không bao giờ tới consumer -> cập nhật dòng lưới cho chúng.
            foreach (var failure in queue.Failures)
            {
                if (!recordMap.TryGetValue(failure.SourcePath, out var rec)) continue;
                Ui(() => rec.TrangThai = ct.IsCancellationRequested
                    ? "Đã dừng"
                    : "❌ " + UserFacingError.ProcessFileFallback);
                if (!ct.IsCancellationRequested)
                {
                    _errorLog.LogException("dat-uy-ban", "StartAsync.UploadFailed:" + Path.GetFileName(failure.SourcePath),
                        new InvalidOperationException(failure.Reason));
                    AddFailedSourcePath(GetOriginalSourcePath(childSourcePaths, failure.SourcePath));
                }
            }

            // Đơn đã publish xong nhưng chưa consumer nào kịp nhận trước khi bị dừng — chỉ cập nhật
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
                    // Trường hợp hiếm: không phải do người dùng dừng mà vẫn còn đơn không ai xử lý.
                    _errorLog.LogException("dat-uy-ban", "StartAsync.StuckItem:" + Path.GetFileName(sourcePath),
                        new InvalidOperationException("File còn dang dở sau khi luồng xử lý đã kết thúc."));
                    AddFailedSourcePath(GetOriginalSourcePath(childSourcePaths, sourcePath));
                    Ui(() => rec.TrangThai = "❌ " + UserFacingError.ProcessFileFallback);
                }
            }

            var splitNote = splitFailed.Count > 0 ? $"  (⚠️ {splitFailed.Count} PDF nguồn tách lỗi)" : "";
            var hasData = Results.Any(IsDone);
            StatusMessage = hasData
                ? $"Hoàn tất: {children.Count} đơn — ✅ {success}, ❌ {failed}.{splitNote} Bấm Export để xuất Excel + PDF con."
                : $"Hoàn tất: ✅ {success}, ❌ {failed}.{splitNote} Không có dữ liệu để xuất.";

            _notifier.NotifyExport(
                Title,
                "Hoàn tất luồng OCR Đất Uỷ Ban",
                _lastRunStartedAt,
                _lastRunCompletedAt,
                success,
                SelectedPath ?? "",
                new[]
                {
                    new ExportReportItem { Name = "Số PDF nguồn", Value = sources.Count.ToString() },
                    new ExportReportItem { Name = "Số đơn tách được", Value = children.Count.ToString() },
                    new ExportReportItem { Name = "Số dòng hoàn thành", Value = success.ToString() },
                    new ExportReportItem { Name = "Số lỗi OCR", Value = failed.ToString() },
                    new ExportReportItem { Name = "Số PDF nguồn tách lỗi", Value = splitFailed.Count.ToString() },
                    new ExportReportItem { Name = "Kết quả", Value = StatusMessage ?? "" }
                });
        }
        catch (Exception ex)
        {
            _errorLog.LogException("dat-uy-ban", "StartAsync.Finalize", ex);
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

    /// <summary>Tách tuần tự từng PDF nguồn thành các PDF con 2 trang; trả về (danh sách PDF con, danh sách file tách lỗi)
    /// và ghi vào <paramref name="childRelDirs"/> thư mục con tương đối (so với thư mục đã chọn) của file nguồn cho từng PDF con.</summary>
    private (List<string> children, List<string> failed) SplitAllSources(
        List<string> sources,
        Dictionary<string, string> childRelDirs,
        Dictionary<string, string> childSourcePaths,
        CancellationToken ct)
    {
        var children = new List<string>();
        var failed = new List<string>();
        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var relDir = GetRelativeDir(src);
                var kids = _split.SplitInto2Pages(src, _opt.TempDir);
                foreach (var k in kids)
                {
                    childRelDirs[k] = relDir;
                    childSourcePaths[k] = src;
                }
                children.AddRange(kids);
            }
            catch (Exception ex)
            {
                _errorLog.LogException("dat-uy-ban", "SplitAllSources:" + Path.GetFileName(src), ex);
                failed.Add(src);
            }
        }
        return (children, failed);
    }

    private static string GetOriginalSourcePath(Dictionary<string, string> childSourcePaths, string childPath) =>
        childSourcePaths.TryGetValue(childPath, out var sourcePath) ? sourcePath : childPath;

    /// <summary>Thư mục con tương đối của file nguồn so với thư mục đã chọn ("" nếu nằm ngay gốc).</summary>
    private string GetRelativeDir(string source)
    {
        if (string.IsNullOrEmpty(SelectedPath)) return "";
        var rel = Path.GetDirectoryName(Path.GetRelativePath(SelectedPath, source));
        return string.IsNullOrEmpty(rel) || rel == "." ? "" : rel;
    }

    /// <summary>Tên hiển thị trên lưới: "&lt;thư mục con nguồn&gt;\&lt;tên PDF con&gt;". Tra map theo <paramref name="mapKey"/> (mặc định chính childPath).</summary>
    private static string DisplayName(Dictionary<string, string> childRelDirs, string childPath, string? mapKey = null)
    {
        var relDir = childRelDirs.TryGetValue(mapKey ?? childPath, out var d) ? d : "";
        var name = Path.GetFileName(childPath);
        return string.IsNullOrEmpty(relDir) ? name : Path.Combine(relDir, name);
    }

    /// <summary>Đổi tên PDF con thành "{thửa}_{tờ}.pdf". Thiếu 1 trong 2 trường → giữ tên cũ. Trùng tên → thêm _1, _2...</summary>
    private static string TryRenameChild(string childPath, string thua, string to, HashSet<string> usedNames)
    {
        thua = Sanitize(thua);
        to = Sanitize(to);
        if (string.IsNullOrEmpty(thua) || string.IsNullOrEmpty(to)) return childPath;

        var dir = Path.GetDirectoryName(childPath)!;
        var baseStem = $"{thua}_{to}";

        string stem, target;
        lock (usedNames)
        {
            stem = baseStem;
            int c = 1;
            while (usedNames.Contains(stem) ||
                   (File.Exists(Path.Combine(dir, stem + ".pdf")) &&
                    !string.Equals(Path.Combine(dir, stem + ".pdf"), childPath, StringComparison.OrdinalIgnoreCase)))
            {
                stem = $"{baseStem}_{c++}";
            }
            usedNames.Add(stem);
            target = Path.Combine(dir, stem + ".pdf");
        }

        if (string.Equals(target, childPath, StringComparison.OrdinalIgnoreCase)) return childPath;
        try { File.Move(childPath, target); return target; }
        catch { return childPath; }
    }

    /// <summary>Bỏ ký tự cấm Windows, gộp khoảng trắng.</summary>
    private static string Sanitize(string? name)
    {
        name = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"[<>:""/\\|?*]", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
        return name;
    }

    [RelayCommand]
    private void Pause() => _cts?.Cancel();

    private static bool IsDone(UyBanRecord r) =>
        r.TrangThai != "Chờ xử lý" &&
        r.TrangThai != "⏳ Đang tải lên..." &&
        r.TrangThai != "⏳ Đang xử lý...";

    private bool CanExport() => !IsRunning && ProgressValue >= 100 && Results.Any(r => r.TrangThai == "Hoàn thành");

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
            _errorLog.LogException("dat-uy-ban", "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var doneRecords = Results.Where(r => r.TrangThai == "Hoàn thành").ToList();
        if (doneRecords.Count == 0) return;
        List<string> successfulSourcePaths;
        lock (_successfulSourcePathsLock)
        {
            successfulSourcePaths = _successfulSourcePaths.ToList();
        }

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        try
        {
            // Lưu cả Excel lẫn PDF con đã đổi tên vào thư mục người dùng vừa chọn.
            Directory.CreateDirectory(destDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var xlsxPath = Path.Combine(destDir, $"UyBan-output_{stamp}.xlsx");

            int copied = await Task.Run(() =>
            {
                _excel.Write(doneRecords, xlsxPath, _opt.TemplateExcel);
                return CopyChildPdfs(doneRecords, destDir);
            }, CancellationToken.None);

            await RecordOcrCreditAsync(Path.GetFileName(xlsxPath), xlsxPath, doneRecords.Count);
            StatusMessage = $"Đã xuất Excel ({doneRecords.Count} dòng) + {copied} PDF con → {destDir}";
            _folderPicker.OpenFolder(destDir);
            DeleteGeminiFilesInBackground(successfulSourcePaths);
        }
        catch (Exception ex)
        {
            _errorLog.LogException("dat-uy-ban", "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private async Task RecordOcrCreditAsync(string tenFile, string duongDanFile, int soTrang)
    {
        var result = await _auth.RecordOcrCreditAsync(tenFile, duongDanFile, soTrang, CancellationToken.None);
        if (result.IsSuccess) return;

        var error = result.Error ?? "Không ghi nhận được hạn mức xử lý.";
        _errorLog.LogException("dat-uy-ban", "ExportAsync.RecordOcrCredit", new InvalidOperationException(error));
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
                _errorLog.LogException("dat-uy-ban", "ExportAsync.DeleteGeminiFiles", ex);
            }
        });
    }

    /// <summary>Copy PDF con (đã đổi tên) của các dòng hoàn thành sang thư mục đích; trả về số file đã copy.</summary>
    private static int CopyChildPdfs(List<UyBanRecord> records, string destDir)
    {
        int n = 0;
        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.FilePath) || !File.Exists(r.FilePath)) continue;
            File.Copy(r.FilePath, Path.Combine(destDir, Path.GetFileName(r.FilePath)), overwrite: true);
            n++;
        }
        return n;
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
