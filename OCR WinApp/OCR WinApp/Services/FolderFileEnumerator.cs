using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OCR_WinApp.Services;

internal static class FolderFileEnumerator
{
    public static IReadOnlyList<string> Enumerate(
        string folder,
        IEnumerable<string> extensions,
        bool recursive)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();

        var allowedExtensions = extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowedExtensions.Count == 0) return Array.Empty<string>();

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(folder, "*", searchOption)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
