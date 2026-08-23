using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Models;

namespace OCR.Business.Ai;

/// <summary>
/// Luồng upload Gemini dùng chung: chuẩn bị artifact → upload (có retry) → đẩy sang hàng đợi
/// cho luồng inference tiêu thụ. Chạy song song với inference nên thời gian upload không
/// còn nằm trên đường găng của cả lô.
/// </summary>
public sealed class GeminiUploadPipeline : IGeminiUploadPipeline
{
    private readonly IGeminiFileApiService _files;
    private readonly GeminiUploadOptions _options;

    public GeminiUploadPipeline(IGeminiFileApiService files, GeminiUploadOptions options)
    {
        _files = files;
        _options = options;
    }

    public IUploadWorkQueue Start(GeminiUploadRequest request, CancellationToken ct)
    {
        var queue = new UploadWorkQueue();

        // Gắn handler NGAY khi vừa tạo queue, TRƯỚC khi phóng producer: producer chạy ngay bằng
        // Task.Run bên dưới, nguồn nào chốt trước khi caller kịp tự "+=" sau Start sẽ mất sự kiện.
        if (request.OnItemSettled is not null)
            queue.ItemSettled += request.OnItemSettled;

        foreach (var path in request.SourcePaths)
            queue.Register(path);

        // Gắn task nền vào queue để Completion phản ánh đúng lúc producer dừng hẳn — nơi gọi
        // (ViewModel) phải await Completion trước khi tổng kết phiên, tránh producer chốt muộn
        // rồi ghi đè ngược trạng thái/tiến độ đã hiển thị "Hoàn tất".
        var producerTask = Task.Run(() => RunProducerAsync(queue, request, ct), CancellationToken.None);
        queue.AttachProducer(producerTask);
        return queue;
    }

    private async Task RunProducerAsync(UploadWorkQueue queue, GeminiUploadRequest request, CancellationToken ct)
    {
        var workers = Math.Max(1, _options.Workers);
        using var slots = new SemaphoreSlim(workers, workers);

        try
        {
            var tasks = request.SourcePaths.Select(path => Task.Run(async () =>
            {
                var entered = false;
                var published = false;
                try
                {
                    await slots.WaitAsync(ct);
                    entered = true;
                    published = await PrepareAndPublishAsync(queue, request, path, ct);
                }
                catch (OperationCanceledException)
                {
                    // Người dùng bấm dừng — vẫn phải chốt ở finally để không treo sổ.
                }
                catch (Exception ex)
                {
                    queue.Settle(path, false, ex.Message);
                }
                finally
                {
                    // Nhả suất TRƯỚC khi chốt: Settle bắn event ItemSettled đồng bộ, subscriber ném
                    // thì suất vẫn phải được trả, nếu không sẽ rò dần tới treo toàn bộ producer.
                    if (entered)
                        slots.Release();

                    // Chưa đẩy được vào hàng đợi nghĩa là sẽ không worker nào xử lý nguồn này.
                    // Settle idempotent nên gọi thừa ở đây vô hại. Nguồn VẪN phải được chốt (để sổ sách
                    // đủ — done == success + failed == tổng file), nhưng chỉ ghi vào Failures khi đây
                    // thật sự là lỗi upload/chuẩn bị: nếu do người dùng bấm Dừng (ct.IsCancellationRequested),
                    // phần lớn nguồn còn nằm ở slots.WaitAsync(ct) sẽ rơi vào đúng nhánh này — với lô
                    // 1400 file đó là ~1390 entry "lỗi" ma trong Failures dù không hề có lỗi upload nào.
                    if (!published)
                        queue.Settle(path, false, "Không chuẩn bị được dữ liệu để gửi.",
                            recordFailure: !ct.IsCancellationRequested);
                }
            })).ToList();

            await Task.WhenAll(tasks);
        }
        catch
        {
            // Lỗi của từng file đã được chốt ở trên; ở đây chỉ đảm bảo chạy tiếp xuống finally.
        }
        finally
        {
            // LỚP CHỐNG TREO QUAN TRỌNG NHẤT: thiếu dòng này, producer chết giữa chừng
            // sẽ khiến toàn bộ worker inference chờ ClaimNextAsync vĩnh viễn.
            queue.CompleteWriter();
        }
    }

    /// <summary>Trả true nếu đã đẩy được việc sang hàng đợi.</summary>
    private async Task<bool> PrepareAndPublishAsync(
        UploadWorkQueue queue, GeminiUploadRequest request, string path, CancellationToken ct)
    {
        // Trúng cache JSON hoặc provider không dùng Files API (OpenRouter) → không upload,
        // vẫn đẩy sang inference để service tự đi đường cũ của nó.
        if (request.WillHitJsonCache?.Invoke(path) == true || !_files.IsEnabled)
        {
            queue.Publish(new UploadWorkItem(path, Array.Empty<GeminiFileReference>(), SkippedUpload: true));
            return true;
        }

        // Chỉ báo "đang tải lên" trên nhánh THẬT SỰ upload — nhánh trúng cache/disabled ở trên đã
        // return sớm nên không bao giờ chạy tới đây. Gọi trước PrepareArtifactsAsync vì đó là bước
        // tốn thời gian nhất (render/tách + upload có retry), cần báo sớm để UI không đứng hình.
        request.OnItemUploading?.Invoke(path);

        var artifacts = await request.PrepareArtifactsAsync(path, ct);
        var uploaded = new List<GeminiFileReference>(artifacts.Count);

        // Giữ nguyên thứ tự artifact: nhánh ảnh nhiều trang phụ thuộc vào thứ tự này.
        foreach (var artifact in artifacts)
            uploaded.Add(await UploadWithRetryAsync(path, artifact, ct));

        queue.Publish(new UploadWorkItem(path, uploaded, SkippedUpload: false));
        return true;
    }

    private async Task<GeminiFileReference> UploadWithRetryAsync(
        string sourcePath, UploadArtifact artifact, CancellationToken ct)
    {
        var attempts = Math.Max(1, _options.MaxRetries);
        var baseDelayMs = Math.Max(0, _options.RetryBaseDelayMs);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _files.GetOrUploadAsync(
                    sourcePath, artifact.ArtifactKey, artifact.DisplayName,
                    artifact.MimeType, artifact.Content, ct);
            }
            catch (Exception) when (attempt < attempts && !ct.IsCancellationRequested)
            {
                if (baseDelayMs > 0)
                    await Task.Delay(baseDelayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
        }
    }
}
