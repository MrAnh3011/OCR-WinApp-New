namespace OCR.Business.Notifications;

public interface IExportResultNotifier
{
    void NotifyExport(
        string screenName,
        string featureName,
        DateTime runStartedAt,
        DateTime runCompletedAt,
        int totalOutput,
        string outputPath,
        IReadOnlyList<ExportReportItem> items);
}
