namespace OCR.Business.SerialRename;

/// <summary>
/// Dựng cây kết quả cho màn "Đổi tên theo Serial": <b>mirror đủ 100% cây nguồn</b>, chỉ những thư mục có
/// GCN đọc được serial mới bị đổi tên.
///
/// Quy tắc cho MỘT thư mục nguồn:
///   - Có N ≥ 1 GCN đọc được serial → sinh N thư mục NGANG HÀNG tên <c>{serial}</c> ở thư mục cha đích.
///     Mỗi thư mục nhãn gồm <c>{serial}-GCN.pdf</c> + MỌI nội dung còn lại của thư mục gốc (file kèm
///     theo, GCN không đọc được serial — giữ nguyên tên, và các thư mục con). N = 1 chính là phép "đổi
///     tên thư mục" thông thường; N ≥ 2 thì nội dung dùng chung được nhân bản vào từng thư mục nhãn,
///     đúng lựa chọn "copy vào MỌI thư mục nhãn" của chủ dự án.
///   - Không có GCN nào đọc được (kể cả thư mục không có GCN) → <b>giữ nguyên tên</b>, copy nguyên
///     nội dung và cấu trúc.
///
/// Cố ý KHÔNG dùng lại <c>IlisUb/GcnTreeExporter</c>: bộ đó THAY thư mục hồ sơ bằng thư mục nhãn và loại
/// mọi file tên chứa "GCN" khỏi tập file kèm theo, nên GCN không đọc được serial sẽ bị mất khỏi cây đích
/// — trái yêu cầu "giữ nguyên tên" của màn này. Tách riêng để sửa màn này không làm vỡ màn iLis-UB.
/// </summary>
public sealed class SerialRenameTreeExporter : ISerialRenameTreeExporter
{
    public SerialRenameExportResult Export(SerialRenameExportRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRoot);

        var sourceRoot = NormalizeRoot(request.SourceRoot);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Không tìm thấy thư mục nguồn: {sourceRoot}");

        var result = new SerialRenameExportResult();

        // Thư mục nguồn → các GCN đọc được serial nằm TRỰC TIẾP trong đó, sắp theo tên file để tất định.
        var readableByFolder = request.Scans
            .Where(s => s.Status == SerialReadStatus.Read
                        && s.Serial.Length > 0
                        && !string.IsNullOrWhiteSpace(s.SourcePath))
            .GroupBy(s => Path.GetDirectoryName(Path.GetFullPath(s.SourcePath)) ?? "",
                     StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SerialScan>)g
                    .OrderBy(s => Path.GetFileName(s.SourcePath), StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var rootPath = AllocateRootPath(request.DestinationRoot, RootFolderName(sourceRoot));
        Directory.CreateDirectory(rootPath);
        result.RootPath = rootPath;

        ExportRoot(sourceRoot, rootPath, readableByFolder, result, ct);
        return result;
    }

    /// <summary>
    /// Thư mục GỐC không bao giờ bị đổi tên. GCN đọc được nằm trực tiếp ở gốc vẫn được cấp thư mục nhãn,
    /// nhưng các file rời của gốc chỉ copy MỘT lần vào gốc đích chứ không nhân bản vào từng nhãn — nhân
    /// bản ở cấp gốc có thể kéo theo hàng loạt file không liên quan.
    /// </summary>
    private static void ExportRoot(
        string srcDir,
        string destDir,
        Dictionary<string, IReadOnlyList<SerialScan>> readableByFolder,
        SerialRenameExportResult result,
        CancellationToken ct)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readable = Readable(readableByFolder, srcDir);
        var readablePaths = ReadablePaths(readable);

        CopyLooseFiles(srcDir, destDir, readablePaths, used, result);

        foreach (var scan in readable)
        {
            ct.ThrowIfCancellationRequested();
            var label = AllocateName(destDir, GcnSerialText.ToFolderName(scan.Serial), used);
            var labelDir = Path.Combine(destDir, label);
            TryRun(result, labelDir, () =>
            {
                Directory.CreateDirectory(labelDir);
                result.LabelFolders++;
                CopyFile(scan.SourcePath, Path.Combine(labelDir, $"{label}-GCN.pdf"));
                result.GcnRenamed++;
            });
        }

        foreach (var sub in SafeDirectories(srcDir, result))
            ExportFolder(sub, destDir, used, readableByFolder, result, ct);
    }

    private static void ExportFolder(
        string srcDir,
        string destParentDir,
        HashSet<string> usedInParent,
        Dictionary<string, IReadOnlyList<SerialScan>> readableByFolder,
        SerialRenameExportResult result,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var readable = Readable(readableByFolder, srcDir);
        var readablePaths = ReadablePaths(readable);
        var subdirs = SafeDirectories(srcDir, result);

        if (readable.Count == 0)
        {
            var name = AllocateName(destParentDir, Path.GetFileName(srcDir), usedInParent);
            var dest = Path.Combine(destParentDir, name);
            var usedInDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TryRun(result, dest, () =>
            {
                Directory.CreateDirectory(dest);
                result.FoldersKeptOriginalName++;
                CopyLooseFiles(srcDir, dest, readablePaths, usedInDest, result);
            });

            foreach (var sub in subdirs)
                ExportFolder(sub, dest, usedInDest, readableByFolder, result, ct);
            return;
        }

        foreach (var scan in readable)
        {
            ct.ThrowIfCancellationRequested();

            var label = AllocateName(destParentDir, GcnSerialText.ToFolderName(scan.Serial), usedInParent);
            var labelDir = Path.Combine(destParentDir, label);
            var gcnName = $"{label}-GCN.pdf";
            var usedInDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gcnName };

            TryRun(result, labelDir, () =>
            {
                Directory.CreateDirectory(labelDir);
                result.LabelFolders++;
                CopyFile(scan.SourcePath, Path.Combine(labelDir, gcnName));
                result.GcnRenamed++;
                // Nội dung còn lại của thư mục gốc, GỒM CẢ GCN không đọc được serial (giữ nguyên tên).
                CopyLooseFiles(srcDir, labelDir, readablePaths, usedInDest, result);
            });

            foreach (var sub in subdirs)
                ExportFolder(sub, labelDir, usedInDest, readableByFolder, result, ct);
        }
    }

    private static IReadOnlyList<SerialScan> Readable(
        Dictionary<string, IReadOnlyList<SerialScan>> map, string dir)
    {
        var key = SafeFullPath(dir);
        return key.Length > 0 && map.TryGetValue(key, out var list) ? list : Array.Empty<SerialScan>();
    }

    private static HashSet<string> ReadablePaths(IReadOnlyList<SerialScan> readable)
        => new(readable.Select(s => SafeFullPath(s.SourcePath)), StringComparer.OrdinalIgnoreCase);

    /// <summary>Copy mọi file nằm trực tiếp trong <paramref name="srcDir"/>, TRỪ các GCN đã có thư mục nhãn riêng.</summary>
    private static void CopyLooseFiles(
        string srcDir,
        string destDir,
        HashSet<string> skipPaths,
        HashSet<string> usedInDest,
        SerialRenameExportResult result)
    {
        foreach (var file in SafeFiles(srcDir, result))
        {
            var full = SafeFullPath(file);
            if (full.Length == 0 || skipPaths.Contains(full)) continue;

            var name = AllocateName(destDir, Path.GetFileName(file), usedInDest);
            var target = Path.Combine(destDir, name);
            TryRun(result, target, () =>
            {
                CopyFile(file, target);
                result.CopiedAsIs++;
            });
        }
    }

    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>
    /// Cấp một tên chưa dùng trong <paramref name="destDir"/>. Trùng thì thêm <c>_2</c>, <c>_3</c>…
    /// (cùng quy ước với thư mục nhãn của màn iLis-UB).
    /// </summary>
    private static string AllocateName(string destDir, string baseName, HashSet<string> used)
    {
        var name = Sanitize(baseName);
        if (name.Length == 0) name = "khong-ro";

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        if (stem.Length == 0) { stem = name; extension = ""; }

        var candidate = name;
        int index = 2;
        while (used.Contains(candidate) || Exists(destDir, candidate))
            candidate = $"{stem}_{index++}{extension}";

        used.Add(candidate);
        return candidate;
    }

    private static bool Exists(string destDir, string name)
    {
        var path = Path.Combine(destDir, name);
        return File.Exists(path) || Directory.Exists(path);
    }

    private static string AllocateRootPath(string destinationRoot, string folderName)
    {
        var name = Sanitize(folderName);
        if (name.Length == 0) name = "output";

        var candidate = Path.Combine(destinationRoot, name);
        int index = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(destinationRoot, $"{name}_{index++}");
        return candidate;
    }

    /// <summary>
    /// Bỏ dấu phân cách cuối NHƯNG giữ nguyên gốc ổ đĩa (<c>E:\</c>): cắt thành <c>E:</c> là *thư mục
    /// hiện hành của ổ E*, làm <c>Path.GetRelativePath</c> lệch tầng. Cùng lý do với màn iLis-UB.
    /// </summary>
    private static string NormalizeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3 && full.EndsWith(Path.DirectorySeparatorChar))
            full = full.TrimEnd(Path.DirectorySeparatorChar);
        return full;
    }

    /// <summary>Tên thư mục gốc để đặt cho cây đích; nguồn là gốc ổ đĩa thì lấy ký tự ổ đĩa.</summary>
    private static string RootFolderName(string sourceRoot)
    {
        var name = Path.GetFileName(sourceRoot);
        if (name.Length > 0) return name;

        var rootPart = Path.GetPathRoot(sourceRoot) ?? "";
        return rootPart.TrimEnd(Path.DirectorySeparatorChar, ':').Trim();
    }

    private static string Sanitize(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '_');
        // Windows tự cắt dấu cách / dấu chấm ở cuối tên thư mục — trim trước để tên tạo ra đúng như tính.
        return text.Trim().TrimEnd('.');
    }

    private static IReadOnlyList<string> SafeFiles(string dir, SerialRenameExportResult result)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Không đọc được danh sách file trong \"{dir}\": {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> SafeDirectories(string dir, SerialRenameExportResult result)
    {
        try
        {
            return Directory.EnumerateDirectories(dir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Không đọc được danh sách thư mục con trong \"{dir}\": {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Một lỗi I/O lẻ (file bị khoá, mất quyền) chỉ mất đúng mục đó, không cuốn theo cả lô.</summary>
    private static void TryRun(SerialRenameExportResult result, string target, Action action)
    {
        try { action(); }
        catch (Exception ex) { result.Warnings.Add($"Lỗi khi ghi \"{target}\": {ex.Message}"); }
    }

    private static string SafeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path); }
        catch { return ""; }
    }
}
