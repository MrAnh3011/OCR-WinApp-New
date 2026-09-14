using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Ai;

namespace OCR.Business.VbdBn;

/// <summary>
/// Luồng trích CCCD/CMND từ file GTK của màn OCR GCN VBD-BN: PDF → AI provider chung →
/// <see cref="CccdEnvelope"/>.
/// </summary>
public interface ICccdExtractService
{
    Task<CccdEnvelope?> ProcessFileAsync(
        string filePath,
        Action<string>? logCallback,
        CancellationToken ct = default,
        string? cachePath = null,
        bool useCachedJson = true,
        IReadOnlyList<GeminiFileReference>? uploadedFiles = null);
}
