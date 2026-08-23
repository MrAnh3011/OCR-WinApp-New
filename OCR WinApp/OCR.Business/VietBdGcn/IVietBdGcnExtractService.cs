using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;

namespace OCR.Business.VietBdGcn;

/// <summary>
/// Gọi LLM trích xuất 1 file (PDF/ảnh) GCN theo prompt RIÊNG của Việt Bản Đồ → envelope JSON.
/// Tách khỏi <c>INewGcnExtractService</c> của màn iLIS: prompt và schema hai màn khác nhau.
/// </summary>
public interface IVietBdGcnExtractService
{
    Task<VietBdGcnEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null);
}
