using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OCR.Business.IlisUb;

/// <summary>
/// Dựng cây thư mục đích cho màn OCR GCN iLis-UB.
///
/// Bất biến quan trọng: KHÔNG đụng vào thư mục nguồn — chỉ đọc. Mọi thứ ghi ra đều nằm dưới
/// <see cref="GcnTreeExportResult.RootPath"/>. Lỗi ở một thư mục hồ sơ không được kéo theo các
/// thư mục còn lại: bắt lỗi tại vòng lặp và cộng vào Warnings.
/// </summary>
public sealed class GcnTreeExporter : IGcnTreeExporter
{
    public GcnTreeExportResult Export(GcnTreeExportRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRoot);

        var sourceRoot = NormalizeSourceRoot(request.SourceRoot);
        var rootPath = AllocateRootDirectory(request.DestinationRoot, RootFolderName(sourceRoot));
        Directory.CreateDirectory(rootPath);

        var warnings = new List<string>();
        int hoSoFolders = 0, labelFolders = 0, gcnCopied = 0, merged = 0, copiedAsIs = 0, missingSerial = 0;

        // Gom theo thư mục CHỨA TRỰC TIẾP file GCN. Sắp theo đường dẫn để kết quả tất định.
        var byFolder = request.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s.SourcePath))
            .GroupBy(s => Path.GetDirectoryName(Path.GetFullPath(s.SourcePath)) ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Tập THƯ MỤC HỒ SƠ (đường dẫn nguồn đầy đủ) — dùng để biết một thư mục con bị loại khỏi diện
        // copy-nguyên có bao giờ trở thành thư mục hồ sơ hay không; nếu không thì phải cảnh báo chứ
        // không để nó biến mất im lặng khỏi cây đích.
        var hoSoFolderPaths = new HashSet<string>(byFolder.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

        // Danh sách file GCN ĐÃ QUÉT lúc PickFolderAsync (gồm cả file OCR lỗi). Bộ lọc đầu vào chạy với
        // GcnKeyword CŨ, còn rules ở đây được nạp LẠI lúc Export — người dùng đổi GcnKeyword giữa hai
        // thời điểm thì file GCN sẽ không còn khớp rules.IsGcnFileName và rơi vào tập "file kèm theo",
        // bị gộp vào {nhãn}-GT.pdf. Giữ danh sách này để loại chúng bằng ĐƯỜNG DẪN, không phụ thuộc rule.
        var scannedGcnPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scanned in request.ScannedGcnPaths)
        {
            if (string.IsNullOrWhiteSpace(scanned)) continue;
            try { scannedGcnPaths.Add(Path.GetFullPath(scanned)); }
            catch (Exception ex) { warnings.Add($"Bỏ qua đường dẫn file đã quét không hợp lệ \"{scanned}\": {ex.Message}"); }
        }

        // Tập đường dẫn đích DÀNH RIÊNG cho cấu trúc cây: mọi destParent của mọi thư mục hồ sơ, cộng
        // toàn bộ thư mục tổ tiên của chúng tính tới rootPath. Không nhãn nào được phép chiếm một
        // trong các đường dẫn này — xem AllocateLabel để biết vì sao đây là bất biến sống còn.
        var reservedDestinationPaths = BuildReservedDestinationPaths(
            rootPath, sourceRoot, byFolder.Select(g => g.Key));

        foreach (var group in byFolder)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var destParent = ResolveDestinationParent(rootPath, sourceRoot, group.Key);
                Directory.CreateDirectory(destParent);

                // Chống trùng nhãn trong PHẠM VI cùng thư mục cha đích.
                var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var folderName = Path.GetFileName(group.Key);

                // Duyệt theo tên file A→Z để thứ tự cấp nhãn tất định.
                var ordered = group.OrderBy(s => Path.GetFileName(s.SourcePath), StringComparer.OrdinalIgnoreCase).ToList();
                var labelDirs = new List<string>(ordered.Count);

                foreach (var source in ordered)
                {
                    var baseLabel = SanitizeLabel(source.Serial);
                    if (baseLabel.Length == 0)
                    {
                        baseLabel = SanitizeLabel(folderName);
                        if (baseLabel.Length == 0) baseLabel = "khong-ro";
                        missingSerial++;
                        warnings.Add($"Không đọc được số serial của \"{Path.GetFileName(source.SourcePath)}\" — giữ tên thư mục gốc \"{baseLabel}\".");
                    }

                    var label = AllocateLabel(destParent, baseLabel, usedLabels, reservedDestinationPaths);
                    var labelDir = Path.Combine(destParent, label);
                    Directory.CreateDirectory(labelDir);
                    labelFolders++;
                    labelDirs.Add(labelDir);

                    if (File.Exists(source.SourcePath))
                    {
                        File.Copy(source.SourcePath, Path.Combine(labelDir, $"{label}-GCN.pdf"), overwrite: true);
                        gcnCopied++;
                    }
                    else
                    {
                        warnings.Add($"Không tìm thấy file GCN nguồn để copy: {source.SourcePath}");
                    }
                }

                hoSoFolders++;

                // Task 4 gắn phần file kèm theo vào đây.
                var attachments = CopyAttachments(
                    group.Key, labelDirs, request.Rules, scannedGcnPaths, hoSoFolderPaths, warnings, ct);
                merged += attachments.MergedFiles;
                copiedAsIs += attachments.CopiedAsIsFiles;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Lỗi khi dựng thư mục \"{group.Key}\": {ex.Message}");
            }
        }

        return new GcnTreeExportResult(
            rootPath, hoSoFolders, labelFolders, gcnCopied, merged, copiedAsIs, missingSerial, warnings);
    }

    /// <summary>
    /// Xử lý toàn bộ "file kèm theo" của một thư mục hồ sơ và đổ vào MỌI thư mục nhãn của nó.
    ///
    /// Kết quả gộp tính ĐÚNG MỘT LẦN cho cả thư mục hồ sơ (ghi ra file tạm) rồi copy bản giống nhau
    /// sang từng thư mục nhãn — nếu gộp lại cho từng nhãn thì n nhãn tốn n lần đọc/ghi cùng dữ liệu.
    /// </summary>
    private static (int MergedFiles, int CopiedAsIsFiles) CopyAttachments(
        string sourceFolder,
        IReadOnlyList<string> labelDirs,
        GcnFolderRules rules,
        HashSet<string> scannedGcnPaths,
        HashSet<string> hoSoFolderPaths,
        List<string> warnings,
        CancellationToken ct)
    {
        if (labelDirs.Count == 0) return (0, 0);

        // Mọi file nằm TRỰC TIẾP trong thư mục hồ sơ, trừ mọi file GCN (kể cả GCN của serial khác
        // và GCN đã OCR lỗi) — chúng không bao giờ được gộp hay copy lẻ vào thư mục nhãn.
        //
        // Điều kiện loại là HỢP của hai vế: nằm trong danh sách file ĐÃ QUÉT (an toàn khi người dùng
        // đổi GcnKeyword giữa lúc quét và lúc Export) HOẶC khớp rules.IsGcnFileName (vẫn loại được file
        // GCN mới xuất hiện trong thư mục sau khi quét).
        var attachments = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(p => !IsGcnAttachment(p, rules, scannedGcnPaths))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Nhóm gộp: chỉ nhận .pdf, sắp theo (chỉ số từ khoá, tên file A→Z) để kết quả lặp lại được.
        var grouped = new Dictionary<int, List<(int KeywordIndex, string Path)>>();
        var leftovers = new List<string>();
        foreach (var file in attachments)
        {
            ct.ThrowIfCancellationRequested();
            var match = Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? rules.Match(Path.GetFileNameWithoutExtension(file))
                : null;

            if (match is null)
            {
                leftovers.Add(file);
                continue;
            }

            if (!grouped.TryGetValue(match.GroupIndex, out var list))
                grouped[match.GroupIndex] = list = new List<(int, string)>();
            list.Add((match.KeywordIndex, file));
        }

        // Thư mục con KHÔNG chứa file GCN nào (đệ quy) thì copy nguyên; thư mục con CÓ GCN là một
        // thư mục hồ sơ độc lập, đã được xử lý ở nhánh riêng nên bỏ qua tại đây.
        var subDirs = Directory.EnumerateDirectories(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(d => !ShouldSkipSubFolder(d, rules, scannedGcnPaths, hoSoFolderPaths, warnings))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tempDir = Path.Combine(Path.GetTempPath(), "ilisub-merge-" + Guid.NewGuid().ToString("N"));
        int mergedFiles = 0, copiedAsIs = 0;

        try
        {
            // Gộp một lần vào thư mục tạm.
            var mergedBySuffix = new List<(string Suffix, string TempPath)>();
            foreach (var groupIndex in grouped.Keys.OrderBy(i => i))
            {
                ct.ThrowIfCancellationRequested();
                var suffix = rules.Groups[groupIndex].Suffix;
                var ordered = grouped[groupIndex]
                    .OrderBy(x => x.KeywordIndex)
                    .ThenBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Path)
                    .ToList();

                var tempPath = Path.Combine(tempDir, $"{suffix}.pdf");
                var result = PdfMerger.Merge(ordered, tempPath, (path, ex) =>
                    warnings.Add($"Bỏ qua file PDF không đọc được khi gộp: {Path.GetFileName(path)} ({ex.Message})"));

                mergedFiles += result.MergedFiles;
                if (result.Pages > 0) mergedBySuffix.Add((suffix, tempPath));
            }

            foreach (var labelDir in labelDirs)
            {
                ct.ThrowIfCancellationRequested();

                // Bọc TỪNG thư mục nhãn: một lỗi I/O lẻ (ví dụ file kèm theo trùng tên với một thư mục
                // con khiến Directory.CreateDirectory ném IOException) nếu để lọt lên tầng Export sẽ
                // huỷ TOÀN BỘ phần file kèm theo của cả thư mục hồ sơ, không chỉ đúng cái tên gây lỗi.
                try
                {
                    var label = Path.GetFileName(labelDir);
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var copiedHere = 0;

                    foreach (var (suffix, tempPath) in mergedBySuffix)
                        File.Copy(tempPath, Path.Combine(labelDir, $"{label}-{suffix}.pdf"), overwrite: true);

                    foreach (var file in leftovers)
                    {
                        var target = UniqueFilePath(Path.Combine(labelDir, Path.GetFileName(file)), used);
                        File.Copy(file, target, overwrite: false);
                        copiedHere++;
                    }

                    foreach (var dir in subDirs)
                        CopyDirectory(dir, Path.Combine(labelDir, Path.GetFileName(dir)), ct);

                    // Đếm theo THƯ MỤC HỒ SƠ (không nhân theo số nhãn) để con số báo cho người dùng khớp
                    // với số file nguồn họ nhìn thấy. Lấy MAX chứ không chia trung bình: nếu một thư mục
                    // nhãn lỗi giữa chừng thì phép chia sẽ ra con số sai lệch vô nghĩa.
                    if (copiedHere > copiedAsIs) copiedAsIs = copiedHere;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"Lỗi khi ghi file kèm theo vào thư mục nhãn \"{labelDir}\" — bỏ qua thư mục nhãn này, " +
                        $"các thư mục nhãn còn lại vẫn được xử lý: {ex.Message}");
                }
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }

        return (mergedFiles, copiedAsIs);
    }

    /// <summary>
    /// Một file nằm trực tiếp trong thư mục hồ sơ có phải file GCN (⇒ KHÔNG được coi là file kèm theo) không.
    ///
    /// HỢP của hai điều kiện: (1) nằm trong danh sách file đã quét lúc chọn thư mục — bộ lọc đầu vào
    /// chạy với <c>GcnKeyword</c> tại thời điểm đó, còn rules được nạp lại lúc Export nên hai hệ quy
    /// chiếu có thể lệch nhau; (2) khớp <see cref="GcnFolderRules.IsGcnFileName"/> — vẫn cần để loại
    /// file GCN mới bỏ vào thư mục SAU khi quét.
    /// </summary>
    private static bool IsGcnAttachment(string path, GcnFolderRules rules, HashSet<string> scannedGcnPaths)
    {
        if (scannedGcnPaths.Count > 0)
        {
            try { if (scannedGcnPaths.Contains(Path.GetFullPath(path))) return true; }
            catch { /* đường dẫn không chuẩn hoá được thì rơi xuống so khớp theo tên bên dưới */ }
        }

        return rules.IsGcnFileName(Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Thư mục con này có bị loại khỏi diện "copy nguyên vào thư mục nhãn" không.
    ///
    /// Loại vì HAI lý do khác hẳn nhau:
    /// - Nó (hoặc một thư mục con cháu của nó) LÀ thư mục hồ sơ ⇒ đã được xử lý ở nhánh riêng theo
    ///   §5.3, bỏ qua là đúng và KHÔNG cần cảnh báo.
    /// - Nó chứa file mang tên GCN nhưng KHÔNG bao giờ trở thành thư mục hồ sơ (ví dụ chỉ có ảnh
    ///   <c>GCN-scan.jpg</c> — bộ lọc đầu vào chỉ nhận .pdf nên không GCN nào trong đó OCR thành công).
    ///   Trước đây thư mục này biến mất khỏi cây đích mà không một cảnh báo nào ⇒ phải cộng cảnh báo.
    /// </summary>
    private static bool ShouldSkipSubFolder(
        string folder,
        GcnFolderRules rules,
        HashSet<string> scannedGcnPaths,
        HashSet<string> hoSoFolderPaths,
        List<string> warnings)
    {
        if (IsOrContainsHoSoFolder(folder, hoSoFolderPaths)) return true;
        if (!ContainsGcnFile(folder, rules, scannedGcnPaths, warnings)) return false;

        warnings.Add(
            $"Bỏ qua thư mục con \"{folder}\": có chứa file mang tên GCN nhưng KHÔNG có GCN nào đọc thành công " +
            $"trong đó, nên không copy sang cây kết quả để tránh copy nhầm dữ liệu GCN vào thư mục nhãn của GCN khác.");
        return true;
    }

    /// <summary>Thư mục này hoặc bất kỳ thư mục con cháu nào của nó có nằm trong tập thư mục hồ sơ không.</summary>
    private static bool IsOrContainsHoSoFolder(string folder, HashSet<string> hoSoFolderPaths)
    {
        string full;
        try { full = Path.GetFullPath(folder); }
        catch { return false; }

        if (hoSoFolderPaths.Contains(full)) return true;

        var prefix = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        foreach (var hoSo in hoSoFolderPaths)
        {
            if (hoSo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Thư mục này (hoặc bất kỳ thư mục con nào, đệ quy) có chứa file GCN không.
    ///
    /// Fail-CLOSED: nếu enumerate ném lỗi (ví dụ mất quyền đọc một thư mục con nằm sâu bên trong,
    /// TRƯỚC khi chạm tới file GCN) thì KHÔNG được coi là "an toàn, không có GCN" — phải coi như
    /// CÓ THỂ chứa GCN (trả về true) để thư mục đó bị loại khỏi <c>subDirs</c> và không bị
    /// <see cref="CopyDirectory"/> copy nhầm vào thư mục nhãn của một GCN khác. Cảnh báo được cộng
    /// vào <paramref name="warnings"/> để người dùng biết có thư mục bị bỏ qua, không im lặng.
    /// </summary>
    private static bool ContainsGcnFile(
        string folder, GcnFolderRules rules, HashSet<string> scannedGcnPaths, List<string> warnings)
        => ContainsGcnFileCore(
            folder,
            () => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories),
            rules,
            scannedGcnPaths,
            warnings);

    /// <summary>
    /// Seam nội bộ: tách phần enumerate ra một delegate để test mô phỏng lỗi đọc thư mục (mất quyền,
    /// ổ đĩa rớt kết nối, ...) mà không cần dựng ACL thật trên đĩa trong CI. Logic fail-closed nằm
    /// TRỌN VẸN ở đây; <see cref="ContainsGcnFile"/> chỉ truyền vào enumerate thật.
    /// </summary>
    internal static bool ContainsGcnFileCore(
        string folder,
        Func<IEnumerable<string>> enumerateFiles,
        GcnFolderRules rules,
        HashSet<string> scannedGcnPaths,
        List<string> warnings)
    {
        try
        {
            // Cùng hệ quy chiếu với IsGcnAttachment: file đã quét HOẶC tên khớp GcnKeyword hiện tại.
            return enumerateFiles().Any(p => IsGcnAttachment(p, rules, scannedGcnPaths));
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"Không đọc được thư mục con \"{folder}\" để kiểm tra có chứa GCN hay không — " +
                $"bỏ qua thư mục này (coi như CÓ THỂ chứa GCN) để tránh copy nhầm dữ liệu: {ex.Message}");
            return true;
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)), ct);
    }

    /// <summary>Chống trùng tên file khi copy: {tên}_{n}{đuôi} — cùng quy ước FailedSourceFileExporter.</summary>
    private static string UniqueFilePath(string target, HashSet<string> used)
    {
        var dir = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        var candidate = target;
        var index = 1;

        while (File.Exists(candidate) || used.Contains(candidate))
            candidate = Path.Combine(dir, $"{name}_{index++}{ext}");

        used.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// Chuẩn hoá thư mục nguồn: bỏ dấu phân cách cuối, NHƯNG giữ nguyên nếu đó là GỐC Ổ ĐĨA.
    ///
    /// Cắt "E:\" thành "E:" là sai nghiêm trọng: Windows hiểu "E:" là *thư mục hiện hành của ổ E* chứ
    /// không phải gốc ổ đĩa, nên <c>Path.GetRelativePath("E:", …)</c> trả đường dẫn tương đối lệch tầng
    /// và <c>destParent</c> bị đặt sai chỗ.
    /// </summary>
    internal static string NormalizeSourceRoot(string sourceRoot)
    {
        var full = Path.GetFullPath(sourceRoot);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0) return full;

        // Gốc ổ đĩa ("E:\") / gốc UNC ("\\\\may\\share\\") thì giữ nguyên cả dấu phân cách cuối.
        var pathRoot = Path.GetPathRoot(full);
        return string.Equals(full, pathRoot, StringComparison.OrdinalIgnoreCase) ? full : trimmed;
    }

    /// <summary>
    /// Tên thư mục gốc của cây đích. Với gốc ổ đĩa <c>Path.GetFileName</c> trả rỗng nên phải lấy ký tự
    /// ổ đĩa ("E:\" → "E") hoặc tên share (UNC "\\\\may\\share" → "share"), thay vì rơi vào "output".
    /// </summary>
    internal static string RootFolderName(string sourceRoot)
    {
        var name = Path.GetFileName(sourceRoot);
        if (name.Length > 0) return name;

        var root = (Path.GetPathRoot(sourceRoot) ?? "")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.Length == 0) return "";

        var shareName = Path.GetFileName(root);          // UNC: tên share
        return shareName.Length > 0 ? shareName : root.TrimEnd(':');
    }

    /// <summary>
    /// Cây đích = D\{tên thư mục nguồn}. Trùng tên thì thêm _2, _3… để hai lần Export không trộn
    /// dữ liệu vào nhau.
    /// </summary>
    private static string AllocateRootDirectory(string destinationRoot, string folderName)
    {
        var name = SanitizeLabel(folderName);
        if (name.Length == 0) name = "output";

        var candidate = Path.Combine(destinationRoot, name);
        var index = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(destinationRoot, $"{name}_{index++}");
        return candidate;
    }

    /// <summary>
    /// Thư mục lá (thư mục hồ sơ) BỊ THAY THẾ bằng các thư mục nhãn, nên thư mục cha đích là ánh xạ
    /// của thư mục CHA của nó. Thư mục nguồn tự nó là thư mục hồ sơ (rel rỗng) → cha đích = root.
    /// </summary>
    private static string ResolveDestinationParent(string rootPath, string sourceRoot, string hoSoFolder)
    {
        var rel = Path.GetRelativePath(sourceRoot, hoSoFolder);
        if (rel == "." || string.IsNullOrWhiteSpace(rel)) return rootPath;

        var relParent = Path.GetDirectoryName(rel);
        return string.IsNullOrWhiteSpace(relParent) ? rootPath : Path.Combine(rootPath, relParent);
    }

    /// <summary>
    /// Mọi đường dẫn đích mà CẤU TRÚC CÂY đã chiếm chỗ: từng <c>destParent</c> của từng thư mục hồ sơ
    /// cộng toàn bộ thư mục tổ tiên của nó tính tới <paramref name="rootPath"/>.
    ///
    /// Cần tập này vì nhãn dự phòng khi thiếu serial CHÍNH LÀ tên thư mục hồ sơ, còn destParent của một
    /// hồ sơ lồng bên trong lại là ánh xạ của thư mục cha nó — hai đường dẫn có thể TRÙNG NHAU tuyệt đối
    /// (hồ sơ "S" thiếu serial + có hồ sơ con bên trong ⇒ nhãn "root\S" ≡ destParent của hồ sơ con).
    /// Khi đó file GCN của hồ sơ con sẽ nằm ngay trong thư mục nhãn của GCN cha — vi phạm bất biến cốt
    /// lõi. <c>Directory.Exists</c> KHÔNG phát hiện được vì lúc cấp nhãn thư mục kia còn chưa tồn tại.
    /// </summary>
    internal static HashSet<string> BuildReservedDestinationPaths(
        string rootPath, string sourceRoot, IEnumerable<string> hoSoFolders)
    {
        var root = Path.GetFullPath(rootPath);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };

        foreach (var folder in hoSoFolders)
        {
            var current = Path.GetFullPath(ResolveDestinationParent(rootPath, sourceRoot, folder));

            // Leo ngược tới root; gặp đường dẫn đã có trong tập thì dừng (tổ tiên phía trên chắc chắn
            // cũng đã được thêm ở lượt trước).
            while (reserved.Add(current))
            {
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent)) break;
                current = parent;
            }
        }

        return reserved;
    }

    private static string AllocateLabel(
        string destParent, string baseLabel, HashSet<string> used, HashSet<string> reservedDestinationPaths)
    {
        var candidate = baseLabel;
        var index = 2;
        while (used.Contains(candidate)
               || Directory.Exists(Path.Combine(destParent, candidate))
               || reservedDestinationPaths.Contains(Path.GetFullPath(Path.Combine(destParent, candidate))))
            candidate = $"{baseLabel}_{index++}";

        used.Add(candidate);
        return candidate;
    }

    /// <summary>Gộp khoảng trắng, thay ký tự không hợp lệ trong tên file/thư mục bằng '_'.</summary>
    internal static string SanitizeLabel(string? value)
    {
        var normalized = string.Join(" ", (value ?? "")
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return "";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '_');

        // Windows tự bỏ dấu chấm/khoảng trắng cuối tên khi tạo thư mục thật, nên phải cắt CẢ HAI
        // (không chỉ dấu chấm) để nhãn giữ trong bộ nhớ luôn khớp tên thư mục thực trên đĩa — ví dụ
        // "abc . " sau khi cắt dấu chấm còn sót lại khoảng trắng cuối nếu chỉ TrimEnd('.').
        return normalized.TrimEnd('.', ' ');
    }
}
