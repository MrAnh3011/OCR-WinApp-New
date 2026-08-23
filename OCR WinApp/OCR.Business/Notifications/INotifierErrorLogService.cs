namespace OCR.Business.Notifications;

public interface INotifierErrorLogService
{
    void LogError(Exception exception, string? accessToken = null, string? recipientId = null);
}
