namespace OCR.Business.Notifications;

public sealed class ExportNotificationOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string RecipientId { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 15;
}
