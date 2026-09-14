using PdfGroupTool.Models;

namespace PdfGroupTool.Services;

public class FileGrouperService
{
    public record ScanResult(List<PdfGroup> Groups, List<string> UnmatchedFiles, int TotalPdfsFound);

    public ScanResult ScanAndGroup(string inputDirectory, bool recursive = true)
    {
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"Thư mục đầu vào không tồn tại: {inputDirectory}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pdfFiles = Directory.GetFiles(inputDirectory, "*.pdf", searchOption);

        var groupsDict = new Dictionary<string, PdfGroup>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();

        foreach (var file in pdfFiles)
        {
            if (PdfClassifier.TryParse(file, out var info) && info != null)
            {
                if (!groupsDict.TryGetValue(info.Serial, out var group))
                {
                    group = new PdfGroup
                    {
                        Serial = info.Serial,
                        FolderName = info.Serial
                    };
                    groupsDict[info.Serial] = group;
                }
                group.Files.Add(info);
            }
            else
            {
                unmatched.Add(file);
            }
        }

        // Sắp xếp các nhóm theo Serial
        var sortedGroups = groupsDict.Values
            .OrderBy(g => g.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Trong mỗi nhóm, ưu tiên sắp xếp: GCN trước, rồi GT, rồi GTK, rồi khác
        foreach (var g in sortedGroups)
        {
            g.Files = g.Files.OrderBy(f => GetTypeOrder(f.DocType))
                             .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }

        return new ScanResult(sortedGroups, unmatched, pdfFiles.Length);
    }

    private static int GetTypeOrder(string docType)
    {
        return docType.ToUpperInvariant() switch
        {
            "GCN" => 1,
            "GT" => 2,
            "GTK" => 3,
            _ => 4
        };
    }

    public async Task ProcessCopyAsync(
        List<PdfGroup> groups,
        string outputDirectory,
        IProgress<ProcessProgressReport>? progress,
        Action<string>? logAction,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);

        int totalGroups = groups.Count;
        int totalFiles = groups.Sum(g => g.FileCount);
        int processedGroups = 0;
        int processedFiles = 0;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            string groupDir = Path.Combine(outputDirectory, group.FolderName);
            Directory.CreateDirectory(groupDir);

            foreach (var file in group.Files)
            {
                ct.ThrowIfCancellationRequested();

                string destPath = Path.Combine(groupDir, file.FileName);
                
                // Copy file bất đồng bộ hoặc File.Copy
                await Task.Run(() =>
                {
                    File.Copy(file.SourcePath, destPath, overwrite: true);
                }, ct);

                file.DestinationPath = destPath;
                file.RelativePath = Path.Combine(".", group.FolderName, file.FileName);
                processedFiles++;
            }

            processedGroups++;
            logAction?.Invoke($"[Gom] Serial '{group.Serial}': Đã gom {group.FileCount} file vào thư mục '{group.FolderName}'");

            progress?.Report(new ProcessProgressReport
            {
                TotalGroups = totalGroups,
                ProcessedGroups = processedGroups,
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
                CurrentMessage = $"Đang xử lý: {processedGroups}/{totalGroups} nhóm ({processedFiles}/{totalFiles} file)"
            });
        }
    }
}

