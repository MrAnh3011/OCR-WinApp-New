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
using OCR.Business.IlisUb;
using OCR.Business.Models;
using OCR.Business.SerialRename;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "Đổi tên theo Serial": chọn thư mục cha → quét đệ quy các PDF có cụm "GCN" trong tên → đọc số
/// serial ở góc dưới phải trang 1 bằng OCR OFFLINE → Export dựng cây kết quả với thư mục/file đã đổi tên
/// theo serial.
///
/// KHÔNG dùng AI ⇒ không trừ quota, không báo Telegram, không cache JSON.
/// Thư mục nguồn CHỈ ĐƯỢC ĐỌC: việc đổi tên chỉ xảy ra ở cây xuất ra, nên đọc sai serial thì dữ liệu gốc
/// vẫn nguyên và chạy lại được.
/// </summary>
public partial class DoiTenSerialViewModel : ObservableObject
{
    internal const string ScreenKey = "doi-ten-serial";

    private readonly IFolderPickerService _folderPicker;
    private readonly ISerialReader _reader;
    private readonly ISerialRenameTreeExporter _treeExporter;
    private readonly IErrorLogService _errorLog;
    private readonly SerialRenameOptions _opt;
    private readonly DispatcherQueue? _dispatcher;
    private readonly object _uiGate = new();

    private CancellationTokenSource? _cts;

    /// <summary>Kết quả đọc, khoá theo đường dẫn nguồn. Thứ tự hiển thị lấy từ <see cref="_gcnFiles"/>.</summary>
    private readonly ConcurrentDictionary<string, SerialScan> _scans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>File GCN theo ĐÚNG thứ tự duyệt cây — quyết định thứ tự lưới.</summary>
    private List<string> _gcnFiles = new();

    public string Title => "Đổi tên theo Serial";

    public string Description =>
        "Chọn thư mục cha → quét đệ quy các file PDF có \"GCN\" trong tên, đọc số serial ở góc dưới phải " +
        "trang 1 bằng OCR offline. Thư mục nguồn KHÔNG bị sửa; bấm Export để dựng cây kết quả đã đổi tên.";

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
    public string FileCountText => $"Tìm thấy {_gcnFiles.Count} file GCN";

    public ObservableCollection<SerialRenameRecord> Results { get; } = new();

    public DoiTenSerialViewModel(
        IFolderPickerService folderPicker,
        ISerialReader reader,
        ISerialRenameTreeExporter treeExporter,
        IErrorLogService errorLog,
        SerialRenameOptions opt)
    {
        _folderPicker = folderPicker;
        _reader = reader;
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
            _gcnFiles = CollectGcnFiles(path, _opt.GcnKeyword);
            _scans.Clear();
            SelectedPath = path;
            ErrorMessage = null;
            ProgressValue = 0;
            StatusMessage = null;

            Results.Clear();
            foreach (var file in _gcnFiles)
                Results.Add(new SerialRenameRecord { FilePath = file, FileName = Relative(path, file) });

            OnPropertyChanged(nameof(FileCountText));
            StartCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            ExportFailedSourcesCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    /// <summary>
    /// Quét đệ quy, chỉ nhận <c>.pdf</c> có cụm <paramref name="keyword"/> trong tên (không kể đuôi),
    /// so khớp bỏ dấu tiếng Việt và không phân biệt hoa/thường qua <see cref="GcnFolderRules.Normalize"/>
    /// — dùng chung đúng một định nghĩa "tên file là GCN" với màn iLis-UB.
    /// Để internal static cho test harness gọi được mà không phải dựng cả ViewModel.
    /// </summary>
    internal static List<string> CollectGcnFiles(string root, string keyword)
    {
        var files = new List<string>();
        if (!Directory.Exists(root)) return files;

        var rules = new GcnFolderRules { GcnKeyword = keyword };
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (!rules.IsGcnFileName(Path.GetFileNameWithoutExtension(path))) continue;
            files.Add(path);
        }
        return files;
    }

    private bool CanStart() => !IsRunning && _gcnFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_gcnFiles.Count == 0 || string.IsNullOrWhiteSpace(SelectedPath)) return;

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
            record.Serial = "";
            record.TenMoi = "";
        }

        int done = 0, read = 0, failed = 0;
        var startedAt = DateTime.Now;

        try
        {
            await Parallel.ForEachAsync(
                _gcnFiles,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _opt.Workers), CancellationToken = ct },
                async (pdfPath, token) =>
                {
                    var record = byPath[pdfPath];
                    Ui(() => record.TrangThai = "⏳ Đang đọc...");

                    SerialScan scan;
                    try
                    {
                        scan = await _reader.ReadAsync(pdfPath, Relative(sourceRoot, pdfPath), token);
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
                        _errorLog.LogException(ScreenKey, "StartAsync.Read:" + Path.GetFileName(pdfPath), ex);
                        scan = new SerialScan
                        {
                            SourcePath = pdfPath,
                            RelativePath = Relative(sourceRoot, pdfPath),
                            Status = SerialReadStatus.OpenError,
                            Note = ex.Message
                        };
                    }

                    _scans[pdfPath] = scan;
                    if (scan.Status == SerialReadStatus.Read) Interlocked.Increment(ref read);
                    else Interlocked.Increment(ref failed);

                    int d = Interlocked.Increment(ref done);
                    Ui(() =>
                    {
                        ApplyScan(record, scan);
                        ProgressValue = (double)d / _gcnFiles.Count * 100;
                        StatusMessage =
                            $"Đang đọc: {d}/{_gcnFiles.Count}  |  ✅ {read} đọc được, ❌ {failed} lỗi{Eta(startedAt, d, _gcnFiles.Count)}";
                    });
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            foreach (var record in Results)
                if (record.TrangThai is "Chờ xử lý" or "⏳ Đang đọc...")
                    record.TrangThai = "Đã dừng";
            StatusMessage = $"Đã dừng: đọc được {done}/{_gcnFiles.Count} file.";
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "StartAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
        finally
        {
            // Luôn chốt trạng thái: đọc dở thì Export vẫn dùng được phần đã có.
            ProgressValue = 100;
            IsRunning = false;
            if (!ct.IsCancellationRequested)
            {
                StatusMessage =
                    $"Hoàn tất: {read} file đọc được serial, {failed} file lỗi. Bấm Export để chọn nơi lưu kết quả.";
            }
            ExportCommand.NotifyCanExecuteChanged();
            ExportFailedSourcesCommand.NotifyCanExecuteChanged();
        }
    }

    private static void ApplyScan(SerialRenameRecord record, SerialScan scan)
    {
        record.Serial = scan.Serial;
        if (scan.Status == SerialReadStatus.Read)
        {
            var folder = GcnSerialText.ToFolderName(scan.Serial);
            record.TenMoi = $"{folder}\\{folder}-GCN.pdf";
            record.TrangThai = "✅ Đọc được serial";
            return;
        }

        record.TenMoi = "(giữ nguyên tên)";
        record.TrangThai = scan.Status == SerialReadStatus.NotFound
            ? "❌ Không tìm thấy serial ở trang 1"
            : "❌ Không mở được PDF";
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
            ErrorMessage = DestinationInsideSourceMessage("kết quả", destDir, sourceRoot!);
            return;
        }

        // Xuất theo ĐÚNG thứ tự duyệt cây, không theo thứ tự đọc xong.
        var scans = _gcnFiles.Where(_scans.ContainsKey).Select(path => _scans[path]).ToList();

        try
        {
            var request = new SerialRenameExportRequest(sourceRoot!, destDir, scans);
            var result = await Task.Run(() => _treeExporter.Export(request), CancellationToken.None);

            foreach (var warning in result.Warnings)
            {
                _errorLog.LogException(ScreenKey, "ExportAsync.Tree",
                    new InvalidOperationException(warning));
            }

            StatusMessage =
                $"Đã xuất: {result.LabelFolders} thư mục đổi tên theo serial, {result.GcnRenamed} file GCN đổi tên, " +
                $"{result.FoldersKeptOriginalName} thư mục giữ nguyên tên, {result.CopiedAsIs} file copy nguyên" +
                (result.Warnings.Count > 0 ? $", {result.Warnings.Count} cảnh báo" : "") +
                $" → {result.RootPath}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private List<string> SnapshotFailedGcnPaths()
        => _gcnFiles
            .Where(path => _scans.TryGetValue(path, out var scan) && scan.NeedsReview)
            .ToList();

    private bool CanExportFailedSources() => !IsRunning && SnapshotFailedGcnPaths().Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportFailedSources))]
    private async Task ExportFailedSourcesAsync()
    {
        var failedPaths = SnapshotFailedGcnPaths();
        if (failedPaths.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return;

        if (IsDestinationInsideSource(SelectedPath, destDir))
        {
            ErrorMessage = DestinationInsideSourceMessage("file lỗi", destDir, SelectedPath ?? "");
            return;
        }

        var successfulPaths = _gcnFiles
            .Where(path => _scans.TryGetValue(path, out var scan) && !scan.NeedsReview)
            .ToList();

        try
        {
            // Dùng lại đúng bộ gom của màn iLis-UB: copy cả thư mục hồ sơ chứa GCN lỗi, loại GCN đã đọc
            // được serial và loại thư mục con vốn đã xử lý xong.
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

            StatusMessage = $"Đã xuất {copied} file từ {collected.HoSoFolders} thư mục hồ sơ lỗi vào: {destDir}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportFailedSourcesAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
        }
    }

    private static string DestinationInsideSourceMessage(string what, string destDir, string sourceRoot)
        => $"Thư mục lưu {what} (\"{destDir}\") đang nằm trong hoặc chính là thư mục nguồn (\"{sourceRoot}\"). " +
           "Hãy chọn một thư mục nằm NGOÀI thư mục nguồn: màn này quét đệ quy nên nếu ghi vào giữa dữ liệu " +
           "gốc, lần quét sau sẽ nhận luôn các file vừa sinh làm đầu vào.";

    /// <summary>
    /// Thư mục đích có nằm TRONG (hoặc chính là) thư mục nguồn không — cùng phép kiểm với màn iLis-UB.
    /// Để internal static cho test harness gọi được mà không phải dựng cả ViewModel.
    /// </summary>
    internal static bool IsDestinationInsideSource(string? sourceRoot, string? destination)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destination)) return false;

        try
        {
            // Chuẩn hoá dấu phân cách CUỐI cho cả hai vế rồi so tiền tố: "E:\HoSo2" không bị nhận nhầm là
            // nằm trong "E:\HoSo"; phép so tiền tố cũng phủ luôn trường hợp hai đường dẫn TRÙNG NHAU.
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
