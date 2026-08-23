using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.Notifications;
using OCR.Business.Split;
using OCR.Business.UyBan;
using OCR_WinApp.Services;

// Fake cho các dependency của 3 ViewModel (GcnNewViewModel, TachGcnViewModel, DatUyBanViewModel)
// dùng riêng cho test full-flow ở ViewModelTests.cs. Mỗi fake nhận một handler cấu hình được theo
// từng đường dẫn để test dựng đúng kịch bản (thành công/lỗi/treo tới khi bị huỷ) mà không cần
// đụng tới AI/HTTP thật.

/// <summary>Chọn thư mục nguồn theo hàng đợi định sẵn; chọn thư mục đích (Export) không dùng ở các
/// test hiện tại nên luôn trả về null (người dùng huỷ) trừ khi có mục kế tiếp trong hàng đợi.</summary>
sealed class FakeFolderPickerService : IFolderPickerService
{
    private readonly Queue<string?> _folders;

    public FakeFolderPickerService(params string?[] folders)
        => _folders = new Queue<string?>(folders);

    public List<string> OpenedFolders { get; } = new();

    public Task<string?> PickFolderAsync()
        => Task.FromResult(_folders.Count > 0 ? _folders.Dequeue() : null);

    public IReadOnlyList<string> EnumerateFiles(string folder, IEnumerable<string> extensions, bool recursive)
        => FolderFileEnumerator.Enumerate(folder, extensions, recursive);

    public Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> extensions)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public void OpenFolder(string path) => OpenedFolders.Add(path);
}

/// <summary>Không log ra đâu cả, chỉ ghi lại để test có thể kiểm tra nếu cần.</summary>
sealed class FakeErrorLogService : IErrorLogService
{
    public List<(string ScreenKey, string Source, string Message)> Entries { get; } = new();

    public void LogException(string screenKey, string source, Exception exception)
        => Entries.Add((screenKey, source, exception.Message));
}

/// <summary>No-op, chỉ đếm số lần gọi — các test full-flow không kiểm tra nội dung báo cáo Telegram.</summary>
sealed class FakeExportResultNotifier : IExportResultNotifier
{
    public int CallCount { get; private set; }

    public void NotifyExport(
        string screenName,
        string featureName,
        DateTime runStartedAt,
        DateTime runCompletedAt,
        int totalOutput,
        string outputPath,
        IReadOnlyList<ExportReportItem> items)
        => CallCount++;
}

/// <summary>Luôn trả lời một lựa chọn cố định (mặc định "Dùng dữ liệu cũ") — các test hiện tại
/// dựng workspace mới hoàn toàn mỗi lần nên hộp thoại này thực tế không được hỏi tới.</summary>
sealed class FakeSplitCachePromptService : ISplitCachePromptService
{
    private readonly SplitCacheChoice _choice;

    public FakeSplitCachePromptService(SplitCacheChoice choice = SplitCacheChoice.UseExisting)
        => _choice = choice;

    public Task<SplitCacheChoice> AskAsync() => Task.FromResult(_choice);
}

/// <summary>Fake INewGcnExtractService: hành vi theo file do test cấu hình qua handler (thành
/// công/ném lỗi/treo tới khi bị huỷ) — không đụng AI thật.</summary>
sealed class FakeNewGcnExtractService : INewGcnExtractService
{
    private readonly Func<string, CancellationToken, Task<NewGcnEnvelope?>> _handler;
    private readonly object _gate = new();

    public FakeNewGcnExtractService(Func<string, CancellationToken, Task<NewGcnEnvelope?>> handler)
        => _handler = handler;

    public List<string> Calls { get; } = new();

    public Task<NewGcnEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        lock (_gate) Calls.Add(filePath);
        return _handler(filePath, ct);
    }
}

sealed class FakeNewGcnExcelExporter : INewGcnExcelExporter
{
    private readonly int _rowCount;

    public FakeNewGcnExcelExporter(int rowCount = 1) => _rowCount = rowCount;

    public List<int> WriteCalls { get; } = new();

    public int Write(IEnumerable<NewGcnEnvelope> envelopes, string outputPath, string templatePath)
    {
        var count = 0;
        foreach (var _ in envelopes) count++;
        WriteCalls.Add(count);
        return _rowCount;
    }
}

/// <summary>Fake ISplitGcnService: hành vi theo file do test cấu hình qua handler.</summary>
sealed class FakeSplitGcnService : ISplitGcnService
{
    private readonly Func<string, CancellationToken, Task<SplitGcnResult>> _handler;
    private readonly object _gate = new();

    public FakeSplitGcnService(Func<string, CancellationToken, Task<SplitGcnResult>> handler)
        => _handler = handler;

    public List<string> Calls { get; } = new();

    public Task<SplitGcnResult> SplitAsync(
        string pdfPath,
        LabelAllocator allocator,
        bool normalizePageRotation = false,
        SplitGcnVariant variant = SplitGcnVariant.Standard,
        string? jsonPath = null,
        bool useCachedJson = false,
        CancellationToken ct = default,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        lock (_gate) Calls.Add(pdfPath);
        return _handler(pdfPath, ct);
    }
}

/// <summary>Fake IUyBanExtractService: hành vi theo file do test cấu hình qua handler.</summary>
sealed class FakeUyBanExtractService : IUyBanExtractService
{
    private readonly Func<string, CancellationToken, Task<UyBanRecord>> _handler;
    private readonly object _gate = new();

    public FakeUyBanExtractService(Func<string, CancellationToken, Task<UyBanRecord>> handler)
        => _handler = handler;

    public List<string> Calls { get; } = new();

    public Task<UyBanRecord> ExtractAsync(
        string pdfPath, CancellationToken ct = default, IReadOnlyList<GeminiFileReference>? uploadedFiles = null)
    {
        lock (_gate) Calls.Add(pdfPath);
        return _handler(pdfPath, ct);
    }
}

/// <summary>
/// Fake IUyBanSplitService: mỗi PDF nguồn tạo ĐÚNG <paramref name="childCount"/> PDF con (mặc định 1),
/// đặt tên TẤT ĐỊNH "{tên nguồn không đuôi}.pdf" (hoặc "_{i}" nếu &gt; 1 con) để test tính trước được
/// đường dẫn PDF con — cần thiết để cấu hình lỗi upload vĩnh viễn cho đúng file con ở kịch bản D.
/// </summary>
sealed class FakeUyBanSplitService : IUyBanSplitService
{
    private readonly Func<string, int> _childCount;

    public FakeUyBanSplitService(Func<string, int>? childCount = null)
        => _childCount = childCount ?? (_ => 1);

    public List<(string Source, string OutDir)> Calls { get; } = new();

    public IReadOnlyList<string> SplitInto2Pages(string pdfPath, string outDir)
    {
        Calls.Add((pdfPath, outDir));
        Directory.CreateDirectory(outDir);

        var count = Math.Max(1, _childCount(pdfPath));
        var stem = Path.GetFileNameWithoutExtension(pdfPath);
        var children = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var childPath = Path.Combine(outDir, count == 1 ? $"{stem}.pdf" : $"{stem}_{i + 1}.pdf");
            File.WriteAllBytes(childPath, Array.Empty<byte>());
            children.Add(childPath);
        }
        return children;
    }
}

sealed class FakeUyBanExcelExporter : IUyBanExcelExporter
{
    public List<int> WriteCalls { get; } = new();

    public void Write(IEnumerable<UyBanRecord> records, string outputPath, string templatePath)
    {
        var count = 0;
        foreach (var _ in records) count++;
        WriteCalls.Add(count);
    }
}
