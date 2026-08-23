using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Ai;

public sealed record GeminiFileReference(string Name, string Uri, string MimeType);

/// <summary>
/// Quản lý vòng đời file gửi Gemini: upload, chờ ACTIVE, dùng lại từ cache và xóa sau Export.
/// </summary>
public interface IGeminiFileApiService
{
    bool IsEnabled { get; }

    Task PrepareExecutionAsync(CancellationToken ct = default);

    Task<GeminiFileReference> GetOrUploadAsync(
        string sourcePath,
        string artifactKey,
        string displayName,
        string mimeType,
        byte[] content,
        CancellationToken ct = default);

    Task DeleteBySourcePathsAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken ct = default);
}

internal sealed class DisabledGeminiFileApiService : IGeminiFileApiService
{
    public static DisabledGeminiFileApiService Instance { get; } = new();

    public bool IsEnabled => false;

    public Task PrepareExecutionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<GeminiFileReference> GetOrUploadAsync(
        string sourcePath,
        string artifactKey,
        string displayName,
        string mimeType,
        byte[] content,
        CancellationToken ct = default)
        => throw new System.InvalidOperationException("Gemini Files API không được bật.");

    public Task DeleteBySourcePathsAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
