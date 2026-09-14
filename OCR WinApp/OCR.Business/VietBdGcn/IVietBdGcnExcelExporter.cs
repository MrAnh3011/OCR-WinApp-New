using System;
using System.Collections.Generic;
using OCR.Business.Models;

namespace OCR.Business.VietBdGcn;

/// <summary>Ghi envelope GCN VietBD ra khuôn Excel của Việt Bản Đồ. Trả về số dòng dữ liệu đã ghi.</summary>
public interface IVietBdGcnExcelExporter
{
    /// <param name="maXa">
    /// Mã xã áp cho cả lô, do người dùng nhập ở hộp thoại lúc bấm Bắt đầu — ghi vào cột `B` (DDK_maXa).
    /// Giấy chứng nhận không in mã xã của thửa đất nên không thể lấy từ OCR.
    /// </param>
    /// <param name="resolveTenFile">
    /// Tuỳ chọn: hàm tính giá trị cột `FE` (Tên file quét) cho từng envelope — dùng khi caller muốn ghi
    /// đường dẫn tương đối + danh sách file cùng thư mục thay vì tên file trần. Bỏ trống → giữ hành vi
    /// cũ, dùng thẳng <c>envelope.ten_file</c>.
    /// </param>
    int Write(
        IEnumerable<VietBdGcnEnvelope> envelopes, string outputPath, string templatePath, string? maXa = null,
        Func<VietBdGcnEnvelope, string>? resolveTenFile = null);
}
