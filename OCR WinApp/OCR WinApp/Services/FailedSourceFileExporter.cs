using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OCR_WinApp.Services;

internal static class FailedSourceFileExporter
{
    public static int CopyFiles(IEnumerable<string> sourcePaths, string destinationRoot, string? sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        Directory.CreateDirectory(destinationRoot);
        var copied = 0;
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourcePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(source)) continue;

            var target = GetTargetPath(source, destinationRoot, sourceRoot, usedTargets);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
            copied++;
        }

        return copied;
    }

    private static string GetTargetPath(
        string source,
        string destinationRoot,
        string? sourceRoot,
        HashSet<string> usedTargets)
    {
        var relativePath = TryGetRelativePath(source, sourceRoot) ?? Path.GetFileName(source);
        var target = Path.Combine(destinationRoot, relativePath);

        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(target) ?? destinationRoot;
            target = Path.Combine(dir, Path.GetFileNameWithoutExtension(target) + "_loi" + Path.GetExtension(target));
        }

        return GetUniquePath(target, usedTargets);
    }

    private static string? TryGetRelativePath(string source, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return null;

        try
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.StartsWith("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                return null;
            }

            return relative;
        }
        catch
        {
            return null;
        }
    }

    private static string GetUniquePath(string target, HashSet<string> usedTargets)
    {
        var dir = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        var candidate = target;
        var index = 1;

        while (File.Exists(candidate) || usedTargets.Contains(candidate))
        {
            candidate = Path.Combine(dir, $"{name}_{index++}{extension}");
        }

        usedTargets.Add(candidate);
        return candidate;
    }
}
