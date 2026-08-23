using System.Collections.Generic;
using OCR.Business.Models;

namespace OCR.Business.NewGcn;

/// <summary>Ghi danh sách envelope GCN (New) ra Excel theo template Excel_FormMau_v3.xlsx (sheet "Data").</summary>
public interface INewGcnExcelExporter
{
    /// <returns>Số dòng dữ liệu thực tế đã chèn sau fallback, cảnh báo trùng serial và mở rộng đồng sở hữu.</returns>
    int Write(IEnumerable<NewGcnEnvelope> envelopes, string outputPath, string templatePath);
}
