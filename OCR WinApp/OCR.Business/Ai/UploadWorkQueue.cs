using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

/// <summary>
/// Hàng đợi việc giữa luồng upload và luồng inference.
///
/// Cấp phát không trùng do <see cref="Channel{T}"/> đảm nhiệm: mỗi item chỉ tới đúng một reader.
/// Cố ý KHÔNG tự dựng queue + lock thủ công vì đó là chỗ dễ sinh race tinh vi.
/// Từ điển trạng thái ở đây chỉ để chốt sổ (đảm bảo mỗi nguồn chốt đúng một lần), không dùng để đồng bộ.
/// </summary>
internal sealed class UploadWorkQueue : IUploadWorkQueue
{
    private const int StatePending = 0;
    private const int StateSettled = 1;

    private readonly Channel<UploadWorkItem> _channel = Channel.CreateUnbounded<UploadWorkItem>();
    private readonly ConcurrentDictionary<string, int> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<UploadFailure> _failures = new();
    private Task _producerTask = Task.CompletedTask;

    public event Action<string, bool>? ItemSettled;

    public IReadOnlyList<UploadFailure> Failures => _failures.ToArray();

    /// <summary>Hoàn tất khi producer (được gán qua <see cref="AttachProducer"/>) đã chạy xong.</summary>
    public Task Completion => _producerTask;

    /// <summary>Ghi danh nguồn trước khi producer chạy, để mọi nguồn đều có mặt trong sổ. Lần ghi danh thứ hai là no-op để bảo vệ bất biến "mỗi nguồn chốt đúng một lần".</summary>
    internal void Register(string sourcePath) => _states.TryAdd(sourcePath, StatePending);

    /// <summary>Gắn task nền của producer để <see cref="Completion"/> phản ánh đúng thời điểm producer dừng hẳn.</summary>
    internal void AttachProducer(Task producerTask) => _producerTask = producerTask;

    /// <summary>Đẩy việc đã upload xong sang luồng inference.</summary>
    internal void Publish(UploadWorkItem item) => _channel.Writer.TryWrite(item);

    /// <summary>Đóng hàng đợi. Gọi trong finally của producer để consumer không bao giờ chờ vĩnh viễn.</summary>
    internal void CompleteWriter() => _channel.Writer.TryComplete();

    public async Task<UploadWorkItem?> ClaimNextAsync(CancellationToken ct)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct))
            {
                if (_channel.Reader.TryRead(out var item))
                    return item;
            }
        }
        catch (OperationCanceledException)
        {
            // Người dùng bấm dừng: coi như hết việc, phần chốt sổ do lớp gọi lo.
        }
        return null;
    }

    /// <summary>Consumer chốt: KHÔNG ghi vào <see cref="Failures"/> — lỗi inference tới được đây nghĩa là
    /// đã tới consumer, nơi gọi (ViewModel) tự xử lý/ghi log riêng theo ngữ cảnh màn hình.</summary>
    public bool Settle(UploadWorkItem item, bool success)
        => SettleCore(item.SourcePath, success, null, recordFailure: false);

    /// <summary>
    /// Producer chốt: mặc định có ghi vào <see cref="Failures"/> vì nguồn này không bao giờ tới được
    /// consumer. Truyền <paramref name="recordFailure"/> = false khi lý do chốt là người dùng bấm Dừng
    /// (không phải lỗi upload thật) để không phình <see cref="Failures"/> — XML doc của nó khai đây là
    /// "nguồn hỏng trước khi tới được luồng inference", không phải "nguồn bị huỷ theo yêu cầu".
    /// </summary>
    internal bool Settle(string sourcePath, bool success, string? failureReason, bool recordFailure = true)
        => SettleCore(sourcePath, success, failureReason, recordFailure);

    /// <summary>
    /// Chuyển sang trạng thái cuối bằng CAS: chỉ lần đầu ăn.
    /// Nhờ vậy gọi thừa từ khối finally cũng vô hại, và ViewModel đếm tiến độ tại đúng một chỗ.
    /// </summary>
    private bool SettleCore(string sourcePath, bool success, string? failureReason, bool recordFailure)
    {
        if (!_states.TryUpdate(sourcePath, StateSettled, StatePending))
            return false;

        if (!success && recordFailure)
            _failures.Enqueue(new UploadFailure(sourcePath, failureReason ?? "Không xác định."));

        ItemSettled?.Invoke(sourcePath, success);
        return true;
    }

    /// <summary>
    /// Chốt mọi nguồn còn treo (chưa ai chốt) và TRẢ VỀ đúng danh sách vừa chốt — dùng khi người dùng
    /// bấm dừng giữa chừng và một số nguồn đã publish nhưng chưa consumer nào kịp claim. KHÔNG ghi vào
    /// <see cref="Failures"/>: đây không phải lỗi upload, nơi gọi tự quyết định cách hiển thị (thường
    /// là "Đã dừng") dựa trên đúng danh sách trả về — không suy đoán qua trạng thái hiển thị.
    /// </summary>
    public IReadOnlyList<string> SettleRemaining(bool success, string reason)
    {
        var settled = new List<string>();
        foreach (var sourcePath in _states.Where(kv => kv.Value == StatePending).Select(kv => kv.Key).ToList())
        {
            // CAS trong SettleCore đảm bảo chỉ gom đúng nguồn THẬT SỰ vừa được chốt ở lần gọi này —
            // không bao giờ gom nhầm nguồn đã chốt trước đó (kể cả bởi một lệnh gọi SettleRemaining khác).
            if (SettleCore(sourcePath, success, reason, recordFailure: false))
                settled.Add(sourcePath);
        }
        return settled;
    }
}
