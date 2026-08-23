namespace OCR.Business.IlisUb;

/// <summary>Kết quả gom file của các thư mục hồ sơ có GCN lỗi.</summary>
/// <param name="Files">Đường dẫn tuyệt đối các file cần copy, đã bỏ trùng, thứ tự tất định.</param>
/// <param name="HoSoFolders">Số thư mục hồ sơ được gom (để hiện lên câu thông báo).</param>
/// <param name="Warnings">Thư mục không đọc được — vẫn gom phần còn lại chứ không bỏ cả lô.</param>
public sealed record FailedHoSoCollectResult(
    IReadOnlyList<string> Files,
    int HoSoFolders,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Gom TOÀN BỘ thư mục hồ sơ chứa GCN lỗi của màn iLis-UB, thay vì chỉ mỗi file GCN lỗi — để chạy lại
/// là có đủ cả GT/GTK/ảnh đi kèm.
///
/// "Thư mục hồ sơ" = thư mục CHỨA TRỰC TIẾP file GCN, đúng khái niệm <see cref="GcnTreeExporter"/>
/// dùng khi dựng cây kết quả.
///
/// Hai phép loại trừ, theo yêu cầu chủ dự án (chốt 2026-08-19) — cùng mục đích: chạy lại lô này KHÔNG
/// OCR lại thứ đã đọc xong, không tốn thêm hạn mức:
///   1. File GCN đã OCR THÀNH CÔNG nằm ngay trong thư mục hồ sơ lỗi thì BỎ, phần còn lại vẫn copy.
///   2. Thư mục CON mà bên dưới nó (ở bất kỳ độ sâu) có GCN đã thành công thì BỎ CẢ THƯ MỤC — đó là
///      một hồ sơ độc lập vốn đã xử lý xong. Nếu bản thân nó cũng có GCN lỗi thì nó tự là một thư mục
///      hồ sơ trong danh sách đầu vào và được gom ở lượt riêng, nên không mất file nào.
/// </summary>
public static class FailedHoSoFileCollector
{
    public static FailedHoSoCollectResult Collect(
        IEnumerable<string> failedGcnPaths,
        IEnumerable<string> successfulGcnPaths)
    {
        var successful = new HashSet<string>(
            successfulGcnPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(SafeFullPath)
                .Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        // Thư mục chứa trực tiếp từng file GCN lỗi. Sắp theo đường dẫn để kết quả tất định.
        var hoSoFolders = failedGcnPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetDirectoryName(SafeFullPath(p)) ?? "")
            .Where(dir => dir.Length > 0 && Directory.Exists(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Hai thư mục hồ sơ lồng nhau (cả hai đều có GCN lỗi) thì lượt của thư mục cha đã đi qua thư
        // mục con — visited chặn duyệt lại, seenFiles chặn file trùng.
        var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var folder in hoSoFolders)
            CollectFolder(folder, successful, files, seenFiles, visitedFolders, warnings);

        return new FailedHoSoCollectResult(files, hoSoFolders.Count, warnings);
    }

    private static void CollectFolder(
        string dir,
        HashSet<string> successful,
        List<string> files,
        HashSet<string> seenFiles,
        HashSet<string> visitedFolders,
        List<string> warnings)
    {
        if (!visitedFolders.Add(dir)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var full = SafeFullPath(file);
                // Loại trừ 1: GCN đã đọc được rồi thì không mang đi chạy lại.
                if (full.Length == 0 || successful.Contains(full)) continue;
                if (seenFiles.Add(full)) files.Add(full);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đọc được danh sách file trong \"{dir}\": {ex.Message}");
        }

        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // Loại trừ 2: thư mục con là hồ sơ đã xử lý xong → bỏ cả thư mục.
                if (ContainsSuccessfulGcn(sub, successful)) continue;
                CollectFolder(SafeFullPath(sub), successful, files, seenFiles, visitedFolders, warnings);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đọc được danh sách thư mục con trong \"{dir}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Bên dưới <paramref name="dir"/> (ở bất kỳ độ sâu) có GCN nào đã OCR thành công không.
    /// Cố ý so bằng ĐƯỜNG DẪN chứ không quét lại đĩa: nhanh hơn, và không bị sai kết luận khi thư mục
    /// mất quyền đọc — thiếu quyền thì phép quét sẽ trả "không có" và ta copy lẫn cả hồ sơ đã xong.
    /// </summary>
    private static bool ContainsSuccessfulGcn(string dir, HashSet<string> successful)
    {
        var prefix = WithTrailingSeparator(SafeFullPath(dir));
        if (prefix.Length == 0) return false;
        return successful.Any(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string WithTrailingSeparator(string path)
    {
        if (path.Length == 0) return "";
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return ""; }
    }
}
