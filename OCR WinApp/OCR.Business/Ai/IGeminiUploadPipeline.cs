using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

/// <summary>Luồng upload Gemini dùng chung cho mọi màn AI. Chạy nền, độc lập với luồng inference.</summary>
public interface IGeminiUploadPipeline
{
    /// <summary>Khởi động producer nền và trả về hàng đợi cho luồng inference tiêu thụ.</summary>
    IUploadWorkQueue Start(GeminiUploadRequest request, CancellationToken ct);
}

/// <summary>
/// Hàng đợi việc đã upload xong. Mỗi việc chỉ tới ĐÚNG MỘT worker inference —
/// bảo đảm này đến từ <see cref="System.Threading.Channels.Channel{T}"/>, không phải khoá tự viết.
/// </summary>
public interface IUploadWorkQueue
{
    /// <summary>Chờ và nhận một việc. Trả null khi hàng đợi đã đóng và không còn việc.</summary>
    Task<UploadWorkItem?> ClaimNextAsync(CancellationToken ct);

    /// <summary>
    /// Chốt trạng thái cuối do luồng inference gọi. Idempotent — chỉ lần đầu trả true và bắn <see cref="ItemSettled"/>.
    /// KHÔNG ghi vào <see cref="Failures"/>: lỗi inference đã tới được consumer nên nơi gọi (ViewModel) tự
    /// xử lý/ghi log lỗi đó theo ngữ cảnh màn hình, tránh log trùng và mất thông tin chẩn đoán gốc.
    /// </summary>
    bool Settle(UploadWorkItem item, bool success);

    /// <summary>(sourcePath, success). Bắn đúng một lần cho mỗi nguồn, tại lần chốt thật.</summary>
    event Action<string, bool>? ItemSettled;

    /// <summary>Các nguồn hỏng TRƯỚC KHI tới được luồng inference (lỗi chuẩn bị/upload phía producer).
    /// KHÔNG chứa lỗi phát sinh ở luồng inference — những lỗi đó consumer đã tự log riêng.</summary>
    IReadOnlyList<UploadFailure> Failures { get; }

    /// <summary>
    /// Hoàn tất khi producer đã dừng hẳn (kể cả khi bị hủy giữa chừng). Nơi gọi PHẢI await cái này
    /// TRƯỚC khi tổng kết phiên: nếu không, producer có thể chốt muộn (sau khi phiên đã báo "Hoàn tất")
    /// và ghi đè ngược tiến độ/trạng thái đã hiển thị xong.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// Chốt mọi nguồn còn treo (chưa ai chốt) và TRẢ VỀ đúng danh sách vừa chốt — gọi SAU khi luồng
    /// inference đã thoát hết và SAU khi đã await <see cref="Completion"/>. Dùng khi người dùng bấm
    /// dừng giữa chừng: có thể còn nguồn đã publish xong nhưng chưa consumer nào kịp nhận (do
    /// <see cref="ClaimNextAsync"/> hủy ngay dù còn việc đọc được). Danh sách trả về CHÍNH LÀ nguồn
    /// không ai xử lý — nơi gọi dùng nó để cập nhật dòng lưới, KHÔNG được tự suy đoán qua trạng thái
    /// hiển thị (dễ nhầm file đã xử lý xong nhưng chưa kịp đổi chữ hiển thị thành "còn treo").
    /// </summary>
    IReadOnlyList<string> SettleRemaining(bool success, string reason);
}
