using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.Split;

public sealed record SplitRunWorkspace(
    string ScreenKey,
    string SourceFolder,
    string CacheDir,
    string JsonDir,
    string OutputDir);

public interface ISplitRunCacheService
{
    SplitRunWorkspace GetWorkspace(string screenKey, string sourceFolder);
    string GetJsonPath(SplitRunWorkspace workspace, string pdfPath);
    bool HasJsonCache(SplitRunWorkspace workspace);
    bool HasJsonCache(SplitRunWorkspace workspace, IEnumerable<string> sourceFiles);
    /// <summary>File này có JSON cache còn mới hơn file nguồn không — dùng để bỏ qua upload.</summary>
    bool HasFreshJson(SplitRunWorkspace workspace, string pdfPath);
    Task ResetWorkspaceAsync(SplitRunWorkspace workspace, CancellationToken ct = default);
    Task PrepareOutputAsync(SplitRunWorkspace workspace, CancellationToken ct = default);
    Task DeleteWorkspaceAsync(SplitRunWorkspace workspace, CancellationToken ct = default);
}
