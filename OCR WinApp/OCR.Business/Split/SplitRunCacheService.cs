using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Models;

namespace OCR.Business.Split;

public sealed class SplitRunCacheService : ISplitRunCacheService
{
    private readonly string _root;

    public SplitRunCacheService(SplitGcnOptions options) : this(options.TempDir) { }

    public SplitRunCacheService(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public SplitRunWorkspace GetWorkspace(string screenKey, string sourceFolder)
    {
        var safeScreen = SanitizeSegment(screenKey);
        var fullSource = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = SanitizeSegment(Path.GetFileName(fullSource));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullSource.ToUpperInvariant())))[..12];
        var cacheDir = EnsureInsideRoot(Path.Combine(_root, safeScreen, $"{folderName}_{hash}"));
        return new SplitRunWorkspace(safeScreen, fullSource, cacheDir, cacheDir, Path.Combine(cacheDir, "output"));
    }

    public string GetJsonPath(SplitRunWorkspace workspace, string pdfPath)
    {
        EnsureWorkspace(workspace);
        var relative = Path.GetRelativePath(workspace.SourceFolder, Path.GetFullPath(pdfPath));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("PDF không nằm trong thư mục nguồn của workspace.");

        return Path.Combine(workspace.JsonDir, Path.ChangeExtension(relative, ".json"));
    }

    public bool HasJsonCache(SplitRunWorkspace workspace)
    {
        EnsureWorkspace(workspace);
        return Directory.Exists(workspace.JsonDir) && Directory.EnumerateFiles(workspace.JsonDir, "*.json", SearchOption.AllDirectories).Any();
    }

    public bool HasJsonCache(SplitRunWorkspace workspace, IEnumerable<string> sourceFiles)
    {
        EnsureWorkspace(workspace);
        return sourceFiles.Any(sourceFile => File.Exists(sourceFile) && File.Exists(GetJsonPath(workspace, sourceFile)));
    }

    /// <summary>File này có JSON cache còn mới hơn file nguồn không — dùng để bỏ qua upload.</summary>
    public bool HasFreshJson(SplitRunWorkspace workspace, string pdfPath)
    {
        try
        {
            var jsonPath = GetJsonPath(workspace, pdfPath);
            if (!File.Exists(jsonPath) || !File.Exists(pdfPath)) return false;
            return File.GetLastWriteTimeUtc(jsonPath) >= File.GetLastWriteTimeUtc(pdfPath);
        }
        catch { return false; }
    }

    public Task ResetWorkspaceAsync(SplitRunWorkspace workspace, CancellationToken ct = default) =>
        RecreateAsync(workspace, recreateJson: true, ct);

    public Task PrepareOutputAsync(SplitRunWorkspace workspace, CancellationToken ct = default) =>
        RecreateAsync(workspace, recreateJson: false, ct);

    public async Task DeleteWorkspaceAsync(SplitRunWorkspace workspace, CancellationToken ct = default)
    {
        EnsureWorkspace(workspace);
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(workspace.CacheDir)) Directory.Delete(workspace.CacheDir, recursive: true);
        }, ct);
    }

    private async Task RecreateAsync(SplitRunWorkspace workspace, bool recreateJson, CancellationToken ct)
    {
        EnsureWorkspace(workspace);
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (recreateJson && Directory.Exists(workspace.CacheDir)) Directory.Delete(workspace.CacheDir, recursive: true);
            else if (Directory.Exists(workspace.OutputDir)) Directory.Delete(workspace.OutputDir, recursive: true);
            Directory.CreateDirectory(workspace.JsonDir);
            Directory.CreateDirectory(workspace.OutputDir);
        }, ct);
    }

    private void EnsureWorkspace(SplitRunWorkspace workspace) => EnsureInsideRoot(workspace.CacheDir);

    private string EnsureInsideRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Đường dẫn cache nằm ngoài split-temp.");
        return full;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }
}
