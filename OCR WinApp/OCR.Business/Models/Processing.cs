namespace OCR.Business.Models;

/// <summary>Mô tả một chức năng OCR — dùng để sinh menu side panel và định danh processor.</summary>
public sealed class DocumentFeature
{
    /// <summary>Định danh duy nhất, ví dụ "plain-text".</summary>
    public required string Key { get; init; }

    /// <summary>Tên hiển thị trên menu.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Mã glyph trong font Segoe Fluent Icons.</summary>
    public string Glyph { get; init; } = "";

    public string? Description { get; init; }
}

/// <summary>Tùy chọn khi chạy một lượt OCR.</summary>
public sealed class ProcessOptions
{
    /// <summary>Mã ngôn ngữ OCR (BCP-47), ví dụ "vi", "en".</summary>
    public string Language { get; init; } = "vi";

    /// <summary>Ghi đè file đích nếu đã tồn tại.</summary>
    public bool Overwrite { get; init; } = true;
}

/// <summary>Thông tin tiến độ báo về UI.</summary>
public sealed class ProcessProgress
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public string? CurrentFile { get; init; }
}

public enum FileStatus
{
    Success,
    Failed,
    Skipped
}

/// <summary>Kết quả xử lý của một file trong lượt batch.</summary>
public sealed class FileOutcome
{
    public required string FileName { get; init; }
    public FileStatus Status { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> OutputFiles { get; init; } = new List<string>();
}

/// <summary>Tổng kết một lượt xử lý batch.</summary>
public sealed class ProcessReport
{
    public IReadOnlyList<FileOutcome> Outcomes { get; init; } = new List<FileOutcome>();
    public int Total => Outcomes.Count;
    public int SuccessCount => Outcomes.Count(o => o.Status == FileStatus.Success);
    public int FailedCount => Outcomes.Count(o => o.Status == FileStatus.Failed);
}
