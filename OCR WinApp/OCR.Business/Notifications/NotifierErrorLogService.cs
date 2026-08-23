using System.Diagnostics;
using System.Text;

namespace OCR.Business.Notifications;

public sealed class NotifierErrorLogService : INotifierErrorLogService
{
    private static readonly object SyncRoot = new();
    private readonly string? _logDir;

    public NotifierErrorLogService()
    {
    }

    public NotifierErrorLogService(string logDir)
    {
        _logDir = logDir;
    }

    public void LogError(Exception exception, string? accessToken = null, string? recipientId = null)
    {
        try
        {
            var logDir = _logDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OCR WinApp",
                "error-log");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "error-notifier.log");
            var message = Redact(exception.Message, accessToken, recipientId);
            var entry = $"error notifier {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
        }
        catch (Exception logException)
        {
            Debug.WriteLine("Ghi error notifier log that bai: " + logException.Message);
        }
    }

    private static string Redact(string message, string? accessToken, string? recipientId)
    {
        var result = message ?? "";
        if (!string.IsNullOrWhiteSpace(accessToken))
            result = result.Replace(accessToken, "***", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(recipientId))
            result = result.Replace(recipientId, "***", StringComparison.Ordinal);
        return result;
    }
}
