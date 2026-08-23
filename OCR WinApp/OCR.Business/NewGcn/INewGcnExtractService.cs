using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;
using OCR.Business.Models;

namespace OCR.Business.NewGcn;

/// <summary>Gọi LLM trích xuất 1 file (PDF/ảnh) GCN → envelope JSON đầy đủ.</summary>
public interface INewGcnExtractService
{
    Task<NewGcnEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null);
}
