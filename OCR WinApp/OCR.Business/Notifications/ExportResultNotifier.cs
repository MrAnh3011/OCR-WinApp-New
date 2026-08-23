using System.Text;
using System.Text.Json;
using OCR.Business.Auth;

namespace OCR.Business.Notifications;

public sealed class ExportResultNotifier : IExportResultNotifier
{
    private const int MaxMessageLength = 4096;
    private readonly ExportNotificationOptions _options;
    private readonly IAuthService _auth;
    private readonly INotifierErrorLogService _errorLog;
    private readonly HttpClient _http;

    public ExportResultNotifier(
        ExportNotificationOptions options,
        IAuthService auth,
        INotifierErrorLogService errorLog,
        HttpClient http)
    {
        _options = options;
        _auth = auth;
        _errorLog = errorLog;
        _http = http;
    }

    public void NotifyExport(
        string screenName,
        string featureName,
        DateTime runStartedAt,
        DateTime runCompletedAt,
        int totalOutput,
        string outputPath,
        IReadOnlyList<ExportReportItem> items)
    {
        _ = SendExportReportAsync(new ExportRunReport
        {
            ScreenName = screenName,
            FeatureName = featureName,
            RunStartedAt = runStartedAt,
            RunCompletedAt = runCompletedAt,
            ExportedAt = DateTime.Now,
            TotalOutput = totalOutput,
            OutputPath = outputPath,
            Items = new List<ExportReportItem>(items ?? Array.Empty<ExportReportItem>())
        });
    }

    public async Task SendExportReportAsync(ExportRunReport report, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BaseUrl) ||
            string.IsNullOrWhiteSpace(_options.AccessToken) ||
            string.IsNullOrWhiteSpace(_options.RecipientId))
        {
            return;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/bot{_options.AccessToken}/sendMessage";

            foreach (var part in SplitMessage(BuildMessage(report), MaxMessageLength))
            {
                var payload = new Dictionary<string, object>
                {
                    ["chat_id"] = _options.RecipientId,
                    ["text"] = part,
                    ["disable_web_page_preview"] = true
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json")
                };
                using var response = await _http.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _errorLog.LogError(
                        new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode}"),
                        _options.AccessToken,
                        _options.RecipientId);
                    return;
                }
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _errorLog.LogError(ex, _options.AccessToken, _options.RecipientId);
        }
        catch (Exception ex)
        {
            _errorLog.LogError(ex, _options.AccessToken, _options.RecipientId);
        }
    }

    private string BuildMessage(ExportRunReport report)
    {
        var elapsed = report.RunCompletedAt >= report.RunStartedAt
            ? report.RunCompletedAt - report.RunStartedAt
            : TimeSpan.Zero;
        var sb = new StringBuilder();
        sb.AppendLine("Báo cáo kết quả OCR WinApp");
        sb.AppendLine();
        sb.AppendLine($"User: {GetUserName()}");
        sb.AppendLine($"Bắt đầu: {report.RunStartedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Hoàn thành: {report.RunCompletedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Thời gian chạy: {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
        sb.AppendLine($"Export: {report.ExportedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Màn hình: {report.ScreenName}");
        sb.AppendLine($"Tính năng: {report.FeatureName}");
        sb.AppendLine($"Tổng output: {report.TotalOutput}");
        if (!string.IsNullOrWhiteSpace(report.OutputPath))
            sb.AppendLine($"Thư mục export: {report.OutputPath}");
        foreach (var item in report.Items)
            sb.AppendLine($"{item.Name}: {item.Value}");
        return sb.ToString().TrimEnd();
    }

    private string GetUserName()
    {
        var displayName = _auth.CurrentSession?.DisplayName;
        return !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : _auth.CurrentUser ?? "";
    }

    private static IReadOnlyList<string> SplitMessage(string text, int maxLength)
    {
        var parts = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var length = Math.Min(maxLength, text.Length - index);
            if (index + length < text.Length)
            {
                var breakAt = text.LastIndexOf('\n', index + length - 1, length);
                if (breakAt >= index) length = breakAt - index + 1;
            }
            if (index + length < text.Length &&
                char.IsHighSurrogate(text[index + length - 1]) &&
                char.IsLowSurrogate(text[index + length]))
            {
                length--;
            }
            var part = text.Substring(index, length).TrimEnd('\r', '\n');
            if (part.Length > 0)
                parts.Add(part);
            index += length;
        }
        return parts;
    }
}
