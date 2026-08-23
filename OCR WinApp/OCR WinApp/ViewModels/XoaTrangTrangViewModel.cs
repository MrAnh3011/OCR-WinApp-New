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
using OCR.Business.BlankPage;
using OCR.Business.Models;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "Xóa trang trắng": chọn thư mục → quét đệ quy mọi PDF, phân tích tìm trang trắng (KHÔNG gọi AI,
/// KHÔNG sửa gì trong thư mục nguồn) → Export chọn thư mục đích rồi mới dựng cây kết quả.
///
/// Khác 6 màn AI: không dùng quota, không báo cáo Telegram, không cache JSON — màn này chạy hoàn toàn
/// cục bộ nên không có cơ sở tính phí và cũng không có gì để cache.
/// </summary>
public partial class XoaTrangTrangViewModel : ObservableObject
{
    internal const string ScreenKey = "xoa-trang-trang";

    private readonly IFolderPickerService _folderPicker;
    private readonly IBlankPageScanner _scanner;
    private readonly IBlankPageTreeExporter _treeExporter;
    private readonly IErrorLogService _errorLog;
    private readonly BlankPageOptions _opt;
    private readonly DispatcherQueue? _dispatcher;
    private readonly object _uiGate = new();

    private CancellationTokenSource? _cts;

    /// <summary>Kết quả quét, khoá theo đường dẫn nguồn. Thứ tự hiển thị lấy từ <see cref="_pdfFiles"/>.</summary>
    private readonly ConcurrentDictionary<string, BlankPageScan> _scans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Danh sách PDF theo ĐÚNG thứ tự duyệt cây thư mục — quyết định thứ tự lưới và thứ tự CSV.</summary>
    private List<string> _pdfFiles = new();

    /// <summary>File không phải PDF: không phân tích, chỉ copy nguyên lúc Export để cây đích khớp cây nguồn.</summary>
    private List<string> _otherFiles = new();

    public string Title => "Xóa trang trắng";

    public string Description =>
        "Chọn thư mục chứa hồ sơ → quét đệ quy mọi file PDF và tìm trang trắng. " +
        "Thư mục nguồn KHÔNG bị sửa; bấm Export để dựng cây kết quả sang thư mục khác.";

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

    public string FileCountText => _otherFiles.Count == 0
        ? $"Tìm thấy {_pdfFiles.Count} tệp PDF"
        : $"Tìm thấy {_pdfFiles.Count} tệp PDF, {_otherFiles.Count} tệp khác sẽ được copy nguyên";

    public ObservableCollection<BlankPageRecord> Results { get; } = new();

    public XoaTrangTrangViewModel(
        IFolderPickerService folderPicker,
        IBlankPageScanner scanner,
        IBlankPageTreeExporter treeExporter,
        IErrorLogService errorLog,
        BlankPageOptions opt)
    {
        _folderPicker = folderPicker;
        _scanner = scanner;
        _treeExporter = treeExporter;
        _errorLog = errorLog;
        _opt = opt;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (path is null) return;

        try
        {
            var (pdfs, others) = CollectFiles(path);
            _pdfFiles = pdfs;
            _otherFiles = others;
            _scans.Clear();
            SelectedPath = path;
            ErrorMessage = null;
            ProgressValue = 0;
            StatusMessage = null;

            Results.Clear();
            foreach (var pdf in _pdfFiles)
                Results.Add(new BlankPageRecord { FilePath = pdf, FileName = Relative(path, pdf) });

            RefreshCounts();
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    /// <summary>
    /// Duyệt đệ quy toàn bộ thư mục, tách thành PDF (đem đi phân tích) và file khác (chỉ copy).
    /// Để internal static cho test harness gọi được mà không phải dựng cả ViewModel.
    /// </summary>
    internal static (List<string> Pdfs, List<string> Others) CollectFiles(string root)
    {
        var pdfs = new List<string>();
        var others = new List<string>();
        if (!Directory.Exists(root)) return (pdfs, others);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) pdfs.Add(path);
            else others.Add(path);
        }
        return (pdfs, others);
    }

    private bool CanStart() => !IsRunning && _pdfFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_pdfFiles.Count == 0 || string.IsNullOrWhiteSpace(SelectedPath)) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsRunning = true;
        ErrorMessage = null;
        ProgressValue = 0;
        _scans.Clear();

        var sourceRoot = SelectedPath!;
        var byPath = Results.ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach (var record in Results)
        {
            record.TrangThai = "Chờ xử lý";
            record.TongTrang = "";
            record.SoTrangXoa = "";
            record.CacTrangDaXoa = "";
        }

        int done = 0, blankFiles = 0, pages = 0, failed = 0;
        var startedAt = DateTime.Now;

        try
        {
            await Parallel.ForEachAsync(
                _pdfFiles,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _opt.Workers), CancellationToken = ct },
                async (pdfPath, token) =>
                {
                    var record = byPath[pdfPath];
                    Ui(() => record.TrangThai = "⏳ Đang xử lý...");

                    BlankPageScan scan;
                    try
                    {
                        scan = await _scanner.ScanAsync(pdfPath, Relative(sourceRoot, pdfPath), token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        Ui(() => record.TrangThai = "Đã dừng");
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Lỗi ngoài dự kiến vẫn phải chốt thành một dòng kết quả, nếu không thanh tiến độ
                        // không bao giờ đủ 100% và nút Export bị khoá vĩnh viễn.
                        _errorLog.LogException(ScreenKey, "StartAsync.Scan:" + Path.GetFileName(pdfPath), ex);
                        scan = new BlankPageScan
                        {
                            SourcePath = pdfPath,
                            RelativePath = Relative(sourceRoot, pdfPath),
                            Status = BlankPageStatus.OpenError,
                            Note = ex.Message
                        };
                    }

                    _scans[pdfPath] = scan;
                    if (scan.Status == BlankPageStatus.Removed)
                    {
                        Interlocked.Increment(ref blankFiles);
                        Interlocked.Add(ref pages, scan.BlankPageIndexes.Count);
                    }
                    if (scan.Status == BlankPageStatus.OpenError) Interlocked.Increment(ref failed);

                    int d = Interlocked.Increment(ref done);
                    Ui(() =>
                    {
                        ApplyScan(record, scan);
                        ProgressValue = (double)d / _pdfFiles.Count * 100;
                        StatusMessage =
                            $"Đang quét: {d}/{_pdfFiles.Count}  |  {blankFiles} file có trang trắng, {pages} trang{Eta(startedAt, d, _pdfFiles.Count)}";
                    });
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            foreach (var record in Results)
                if (record.TrangThai is "Chờ xử lý" or "⏳ Đang xử lý...")
                    record.TrangThai = "Đã dừng";
            StatusMessage = $"Đã dừng: quét được {done}/{_pdfFiles.Count} file.";
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "StartAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
        finally
        {
            // Luôn chốt trạng thái: quét dở thì Export vẫn dùng được phần đã có.
            ProgressValue = 100;
            IsRunning = false;
            if (!ct.IsCancellationRequested)
            {
                StatusMessage =
                    $"Hoàn tất: {_scans.Count} file PDF đã quét, {blankFiles} file có trang trắng ({pages} trang), {failed} file lỗi. " +
                    "Bấm Export để chọn nơi lưu kết quả.";
            }
            ExportCommand.NotifyCanExecuteChanged();
            ExportFailedSourcesCommand.NotifyCanExecuteChanged();
        }
    }

    private static void ApplyScan(BlankPageRecord record, BlankPageScan scan)
    {
        record.TongTrang = scan.Status == BlankPageStatus.OpenError ? "" : scan.TotalPages.ToString();
        record.SoTrangXoa = scan.Status == BlankPageStatus.OpenError ? "" : scan.RemovedPageCount.ToString();
        record.CacTrangDaXoa = scan.DescribeBlankPages();
        record.TrangThai = scan.Status switch
        {
            BlankPageStatus.NoBlank => "✅ Không có trang trắng",
            BlankPageStatus.Removed => $"✅ Sẽ xóa {scan.BlankPageIndexes.Count} trang trắng",
            BlankPageStatus.AllBlank => "⚠️ Toàn bộ trang đều trắng — giữ nguyên file gốc, cần kiểm tra tay",
            _ => "❌ Không mở được PDF — sẽ copy nguyên bản gốc"
        };
    }

    [RelayCommand]
    private void Pause() => _cts?.Cancel();

    private bool CanExport() => !IsRunning && ProgressValue >= 100 && !_scans.IsEmpty;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var sourceRoot = SelectedPath;
        if (string.IsNullOrWhiteSpace(sourceRoot) || _scans.IsEmpty) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        if (IsDestinationInsideSource(sourceRoot, destDir))
        {
            ErrorMessage =
                $"Thư mục lưu kết quả (\"{destDir}\") đang nằm trong hoặc chính là thư mục nguồn (\"{sourceRoot}\"). " +
                "Hãy chọn một thư mục nằm NGOÀI thư mục nguồn: nếu không, cây kết quả sẽ bị ghi lẫn vào dữ liệu gốc " +
                "và lần quét sau sẽ nhận luôn các file vừa sinh làm đầu vào.";
            return;
        }

        // Xuất theo ĐÚNG thứ tự duyệt cây thư mục, không theo thứ tự quét xong.
        var scans = _pdfFiles
            .Where(_scans.ContainsKey)
            .Select(path => _scans[path])
            .ToList();

        try
        {
            var request = new BlankPageExportRequest(sourceRoot!, destDir, scans, _otherFiles);
            var result = await Task.Run(() => _treeExporter.Export(request), CancellationToken.None);

            StatusMessage =
                $"Đã xuất: {result.PdfCleaned} PDF bỏ trang trắng ({result.PagesRemoved} trang), " +
                $"{result.PdfCopied} PDF copy nguyên, {result.OtherCopied} tệp khác" +
                (result.Failed > 0 ? $", {result.Failed} tệp lỗi" : "") +
                $". Báo cáo: {Path.GetFileName(result.ReportPath)}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    /// <summary>PDF hỏng và PDF bị coi là trắng toàn bộ — đều là file người dùng phải xem bằng tay.</summary>
    private List<string> SnapshotFilesNeedingReview()
        => _pdfFiles
            .Where(path => _scans.TryGetValue(path, out var scan) && scan.NeedsReview)
            .ToList();

    private bool CanExportFailedSources() => !IsRunning && SnapshotFilesNeedingReview().Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportFailedSources))]
    private async Task ExportFailedSourcesAsync()
    {
        var paths = SnapshotFilesNeedingReview();
        if (paths.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return;

        if (IsDestinationInsideSource(SelectedPath, destDir))
        {
            ErrorMessage =
                $"Thư mục lưu file lỗi (\"{destDir}\") đang nằm trong hoặc chính là thư mục nguồn (\"{SelectedPath}\"). " +
                "Hãy chọn một thư mục nằm NGOÀI thư mục nguồn để không trộn lẫn vào dữ liệu gốc.";
            return;
        }

        try
        {
            var copied = await Task.Run(
                () => FailedSourceFileExporter.CopyFiles(paths, destDir, SelectedPath),
                CancellationToken.None);
            StatusMessage = $"Đã xuất {copied} file cần kiểm tra vào: {destDir}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    /// <summary>
    /// Thư mục lưu kết quả có nằm TRONG (hoặc chính là) thư mục nguồn không — cùng phép kiểm với màn
    /// iLis-UB. Phải chặn vì màn này quét đệ quy: ghi cây kết quả vào giữa thư mục nguồn thì lần quét
    /// sau nuốt luôn các file vừa sinh làm nguồn mới.
    /// </summary>
    internal static bool IsDestinationInsideSource(string? sourceRoot, string? destination)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destination)) return false;

        try
        {
            // Chuẩn hoá dấu phân cách CUỐI cho cả hai vế rồi so tiền tố: có dấu phân cách cuối thì
            // "E:\HoSo2" không bị nhận nhầm là nằm trong "E:\HoSo"; phép so tiền tố cũng phủ luôn
            // trường hợp hai đường dẫn TRÙNG NHAU.
            return WithTrailingSeparator(destination).StartsWith(
                WithTrailingSeparator(sourceRoot), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string WithTrailingSeparator(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    private static string Relative(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            return string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal)
                ? Path.GetFileName(path)
                : relative;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(FileCountText));
        StartCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ExportFailedSourcesCommand.NotifyCanExecuteChanged();
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
    /// chồng lấn nhau. Xem chú thích đầy đủ ở <c>GcnNewViewModel.Ui</c> — nhánh không có dispatcher
    /// (chạy ngoài WinUI, VD test harness) BẮT BUỘC phải khoá, vì nhiều worker cùng sửa
    /// <see cref="Results"/> song song sẽ ném lỗi giữa luồng xử lý.
    /// </summary>
    private void Ui(Action action)
    {
        if (_dispatcher is null) { lock (_uiGate) action(); }
        else if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }
}
