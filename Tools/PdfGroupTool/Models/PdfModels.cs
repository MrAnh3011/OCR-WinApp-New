namespace PdfGroupTool.Models;

public class PdfFileInfo
{
    public required string SourcePath { get; set; }
    public required string FileName { get; set; }
    public required string Serial { get; set; }
    public required string DocType { get; set; }
    public string? DestinationPath { get; set; }
    public string? RelativePath { get; set; }
}

public class PdfGroup
{
    public required string Serial { get; set; }
    public required string FolderName { get; set; }
    public List<PdfFileInfo> Files { get; set; } = new();

    public int FileCount => Files.Count;
}

public class ProcessProgressReport
{
    public int TotalGroups { get; set; }
    public int ProcessedGroups { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string CurrentMessage { get; set; } = string.Empty;
}

