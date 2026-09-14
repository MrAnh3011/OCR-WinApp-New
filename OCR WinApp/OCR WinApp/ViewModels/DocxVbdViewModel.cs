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
using OCR.Business.DocxVbd;
using OCR.Business.Models;
using OCR.Business.VietBdGcn;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Màn "Convert docx to Excel VBD": chọn thư mục chứa các file .docx "Sổ cấp GCN" (mẫu Q1 An Lạc) →
/// parse THUẦN CODE (<see cref="IDocxVbdConvertService"/>, mỗi trang sổ = một GCN) → Export ra Excel
/// khuôn Việt Bản Đồ qua <see cref="IVietBdGcnExcelExporter"/> dùng chung với màn OCR GCN VietBD.
///
/// KHÁC các màn OCR AI: KHÔNG gọi API, KHÔNG upload Gemini, KHÔNG cache JSON và KHÔNG ghi hạn mức
/// OCR (không có gì để trừ). Mã xã vẫn hỏi một lần lúc Bắt đầu (giấy/sổ không in mã xã) và ghi vào
/// cột B lúc Export, giống màn VietBD.
/// </summary>
public partial class DocxVbdViewModel : ObservableObject
{
    /// <summary>Screen key: error log `error-docx-vbd.log`.</summary>
    private const string ScreenKey = "docx-vbd";

    private static readonly string[] DocxExtensions = { ".docx" };

    private readonly IFolderPickerService _folderPicker;
    private readonly IDocxVbdConvertService _convert;
    private readonly IVietBdGcnExcelExporter _excel;
    private readonly IErrorLogService _errorLog;
    private readonly IMaXaPromptService _maXaPrompt;
    private readonly DocxVbdOptions _opt;
    private readonly DispatcherQueue? _dispatcher;

    private CancellationTokenSource? _cts;
    private readonly List<VietBdGcnEnvelope> _envelopes = new();
    private readonly object _envelopesLock = new();
    private readonly HashSet<string> _failedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedSourcePathsLock = new();

    /// <summary>Mã xã người dùng nhập lúc bấm Bắt đầu — giữ lại giữa các phiên để điền sẵn ô nhập.</summary>
    private string? _maXa;

    public string Title => "Convert docx to Excel VBD";
    public string Description => "Chọn thư mục chứa file docx Sổ cấp GCN (mỗi trang = một GCN) → convert thuần code, không gọi API OCR → Export Excel khuôn Việt Bản Đồ.";

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
    public string FileCountText => $"Tìm thấy {SelectedFiles.Count} tệp docx";

    public ObservableCollection<SelectedFile> SelectedFiles { get; } = new();
    public ObservableCollection<VietBdGcnRecord> Results { get; } = new();

    public DocxVbdViewModel(
        IFolderPickerService folderPicker,
        IDocxVbdConvertService convert,
        IVietBdGcnExcelExporter excel,
        IErrorLogService errorLog,
        IMaXaPromptService maXaPrompt,
        DocxVbdOptions opt)
    {
        _folderPicker = folderPicker;
        _convert = convert;
        _excel = excel;
        _errorLog = errorLog;
        _maXaPrompt = maXaPrompt;
        _opt = opt;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        SelectedFiles.CollectionChanged += (_, _) =>
        {
            StartCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FileCountText));
        };
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private bool CanPickFolder => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanPickFolder))]
    private async Task PickFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (path is null) return;
        try
        {
            ResetSelection();
            AddFolder(path);
            SelectedPath = path;
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "PickFolderAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
    }

    /// <summary>Quét đệ quy .docx trong thư mục gốc; bỏ file khoá tạm "~$..." Word sinh ra khi đang mở.</summary>
    private void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in _folderPicker.EnumerateFiles(folder, DocxExtensions, recursive: true))
        {
            if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) continue;
            if (SelectedFiles.Any(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
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
        if (SelectedFiles.Count == 0) return;

        // Sổ không in mã xã của thửa đất nên cột B chỉ có thể lấy từ người dùng — hỏi TRƯỚC khi khoá
        // UI chạy lô, bấm Hủy/bỏ trống = không chạy (giữ nguyên trạng thái để bấm Bắt đầu lại).
        var maXa = await _maXaPrompt.AskAsync(_maXa);
        if (string.IsNullOrWhiteSpace(maXa)) return;
        _maXa = maXa.Trim();

        ErrorMessage = null;
        Results.Clear();
        lock (_envelopesLock)
        {
            _envelopes.Clear();
        }
        ClearFailedSourcePaths();
        IsRunning = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var selected = SelectedFiles.ToList();
        var stubMap = new Dictionary<string, VietBdGcnRecord>();
        foreach (var sf in selected)
        {
            var stub = new VietBdGcnRecord { FilePath = sf.Path, FileName = sf.Name, TrangThai = "Chờ xử lý" };
            Results.Add(stub);
            stubMap[sf.Path] = stub;
        }

        int done = 0, success = 0, failed = 0;
        var startTime = DateTime.Now;

        try
        {
            // Parse cục bộ rất nhanh (không I/O mạng) — một task nền chạy tuần tự là đủ, không cần worker.
            await Task.Run(() =>
            {
                foreach (var sf in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    var stub = stubMap[sf.Path];
                    try
                    {
                        Ui(() => stub.TrangThai = "⏳ Đang xử lý...");

                        var envelopes = _convert.ConvertFile(sf.Path);
                        if (envelopes.Count == 0)
                            throw new InvalidDataException("Không tìm thấy trang sổ GCN nào đúng mẫu trong file.");

                        lock (_envelopesLock)
                        {
                            _envelopes.AddRange(envelopes);
                        }
                        success++;
                        Ui(() =>
                        {
                            stub.TrangThai = "Thành công";
                            Results.Remove(stub);
                            foreach (var envelope in envelopes) AddEnvelopeRows(sf.Path, envelope);
                        });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _errorLog.LogException(ScreenKey, "StartAsync.ConvertFile:" + Path.GetFileName(sf.Path), ex);
                        AddFailedSourcePath(sf.Path);
                        Ui(() => stub.TrangThai = "❌ " + UserFacingError.ProcessFile(ex));
                    }

                    done++;
                    int d = done, s = success, f = failed;
                    Ui(() =>
                    {
                        ProgressValue = (double)d / selected.Count * 100;
                        StatusMessage = $"Đang xử lý: {d}/{selected.Count}  |  ✅ {s}  ❌ {f}";
                    });
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Người dùng bấm Dừng — không phải lỗi, không ghi log.
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "StartAsync", ex);
            ErrorMessage = UserFacingError.Prepare(ex);
        }
        finally
        {
            try
            {
                foreach (var sf in selected)
                {
                    var stub = stubMap[sf.Path];
                    if (stub.TrangThai is "Chờ xử lý" or "⏳ Đang xử lý...")
                        Ui(() => stub.TrangThai = "Đã dừng");
                }

                // Ép 100% để Export bật được kể cả khi dừng giữa chừng mà đã có dữ liệu (giống các màn khác).
                if (selected.Count > 0) ProgressValue = 100;

                int pageCount, parcelCount;
                lock (_envelopesLock)
                {
                    pageCount = _envelopes.Count;
                    parcelCount = _envelopes.Sum(e => Math.Max(1, e.danh_sach_dong?.Count ?? 0));
                }
                var elapsed = DateTime.Now - startTime;
                StatusMessage = pageCount > 0
                    ? $"Hoàn tất trong {elapsed.TotalSeconds:0} giây: {success} file thành công, {failed} lỗi — {pageCount} trang GCN / {parcelCount} thửa. Bấm Export để xuất Excel."
                    : $"Hoàn tất: {success} file thành công, {failed} lỗi. Không có dữ liệu để xuất.";
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
    }

    /// <summary>Mỗi thửa (danh_sach_dong) → 1 dòng lưới; trạng thái nêu số cảnh báo để người dùng rà cột FG.</summary>
    private void AddEnvelopeRows(string file, VietBdGcnEnvelope envelope)
    {
        var info = envelope.thong_tin_gcn;
        var chu = string.Join("; ", (info.chu_su_dung_chi_tiet ?? new()).Select(c => c.ho_ten ?? ""));
        int warningCount = info.canh_bao?.Count ?? 0;
        var trangThai = warningCount > 0 ? $"⚠ {warningCount} cảnh báo" : "Thành công";
        var displayName = envelope.ten_file;

        var rows = envelope.danh_sach_dong ?? new();
        if (rows.Count == 0)
        {
            Results.Add(new VietBdGcnRecord
            {
                FilePath = file,
                FileName = displayName,
                TrangThai = $"{trangThai} - không có dòng thửa đất",
                SoSerial = info.so_serial ?? "",
                ChuSuDung = chu
            });
            return;
        }

        foreach (var r in rows)
        {
            Results.Add(new VietBdGcnRecord
            {
                FilePath = file,
                FileName = displayName,
                TrangThai = trangThai,
                SoSerial = info.so_serial ?? "",
                ChuSuDung = chu,
                SoThua = r.td_so_thua ?? "",
                SoTo = r.td_so_to ?? "",
                TongDienTich = r.td_tong_dien_tich ?? "",
                LoaiDat = r.muc_dich_su_dung?.FirstOrDefault()?.ma_mdsd ?? ""
            });
        }
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

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        List<VietBdGcnEnvelope> snapshot;
        lock (_envelopesLock)
        {
            snapshot = _envelopes.ToList();
        }
        if (snapshot.Count == 0) return;

        var destDir = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(destDir)) return; // người dùng huỷ chọn thư mục

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outPath = Path.Combine(destDir, $"Convert-Docx-VBD-output_{stamp}.xlsx");

            var rows = await Task.Run(
                () => _excel.Write(snapshot, outPath, _opt.TemplateExcel, _maXa),
                CancellationToken.None);

            // Màn này không gọi API OCR nên KHÔNG ghi nhận hạn mức.
            StatusMessage = $"Đã xuất {rows} dòng → {outPath}";
            _folderPicker.OpenFolder(destDir);
        }
        catch (Exception ex)
        {
            _errorLog.LogException(ScreenKey, "ExportAsync", ex);
            ErrorMessage = UserFacingError.Export(ex);
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

    /// <summary>
    /// Chạy <paramref name="action"/> theo ngữ nghĩa "trên UI thread" — cùng bất biến với các màn khác:
    /// có dispatcher thì enqueue, không có (test harness) thì khoá tuần tự để không sửa song song
    /// <see cref="Results"/> (ObservableCollection không thread-safe).
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
