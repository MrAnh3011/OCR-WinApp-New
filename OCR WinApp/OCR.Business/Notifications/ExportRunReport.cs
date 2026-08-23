namespace OCR.Business.Notifications;

public sealed class ExportRunReport
{
    public string ScreenName { get; init; } = "";
    public string FeatureName { get; init; } = "";
    public DateTime RunStartedAt { get; init; }
    public DateTime RunCompletedAt { get; init; }
    public DateTime ExportedAt { get; init; }
    public int TotalOutput { get; init; }
    public string OutputPath { get; init; } = "";
    public List<ExportReportItem> Items { get; init; } = new();
}

public sealed class ExportReportItem
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
}
