using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OCR.Business.NewGcn;

public sealed record NewGcnRunWorkspace(
    string SourceFolder,
    string CacheDir,
    string JsonDir);

public sealed class NewGcnRunCacheService
{
    private readonly string _root;

    public NewGcnRunCacheService(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string GetResponseCachePath(string filePath)
    {
        var fullFile = Path.GetFullPath(filePath);
        var sourceFolder = Path.GetDirectoryName(fullFile)
            ?? throw new InvalidOperationException("Khong xac dinh duoc thu muc nguon OCR GCN New.");
        var folderName = SanitizeSegment(Path.GetFileName(sourceFolder));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceFolder.ToUpperInvariant())))[..12];
        var cacheDir = EnsureInsideRoot(Path.Combine(_root, $"{folderName}_{hash}", "response_new"));
        return Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(fullFile) + ".json");
    }

    public NewGcnRunWorkspace GetWorkspace(string screenKey, string sourceFolder)
    {
        var safeScreen = SanitizeSegment(screenKey);
        var fullSource = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = SanitizeSegment(Path.GetFileName(fullSource));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullSource.ToUpperInvariant())))[..12];
        var cacheDir = EnsureInsideRoot(Path.Combine(_root, safeScreen, $"{folderName}_{hash}"));
        return new NewGcnRunWorkspace(fullSource, cacheDir, cacheDir);
    }

    public string GetJsonPath(NewGcnRunWorkspace workspace, string filePath)
    {
        EnsureInsideRoot(workspace.CacheDir);
        var relative = Path.GetRelativePath(workspace.SourceFolder, Path.GetFullPath(filePath));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("File không nằm trong thư mục nguồn OCR GCN New.");

        return Path.Combine(workspace.JsonDir, Path.ChangeExtension(relative, ".json"));
    }

    public bool HasJsonCache(NewGcnRunWorkspace workspace)
    {
        EnsureInsideRoot(workspace.CacheDir);
        return Directory.Exists(workspace.JsonDir) &&
               Directory.EnumerateFiles(workspace.JsonDir, "*.json", SearchOption.AllDirectories).Any();
    }

    public bool HasJsonCache(NewGcnRunWorkspace workspace, IEnumerable<string> sourceFiles)
    {
        EnsureInsideRoot(workspace.CacheDir);
        return sourceFiles.Any(sourceFile => File.Exists(sourceFile) && File.Exists(GetJsonPath(workspace, sourceFile)));
    }

    /// <summary>File này có JSON cache còn mới hơn file nguồn không — dùng để bỏ qua upload.</summary>
    public bool HasFreshJson(NewGcnRunWorkspace workspace, string sourcePath)
    {
        try
        {
            var jsonPath = GetJsonPath(workspace, sourcePath);
            if (!File.Exists(jsonPath) || !File.Exists(sourcePath)) return false;
            return File.GetLastWriteTimeUtc(jsonPath) >= File.GetLastWriteTimeUtc(sourcePath);
        }
        catch { return false; }
    }

    public Task ResetWorkspaceAsync(NewGcnRunWorkspace workspace, CancellationToken ct = default)
    {
        EnsureInsideRoot(workspace.CacheDir);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(workspace.CacheDir)) Directory.Delete(workspace.CacheDir, recursive: true);
            Directory.CreateDirectory(workspace.CacheDir);
        }, ct);
    }

    private string EnsureInsideRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Duong dan cache nam ngoai newgcn-temp.");
        return full;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }
}
