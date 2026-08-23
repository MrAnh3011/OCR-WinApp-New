using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Models;

namespace OCR.Business.Ai;

/// <summary>
/// Triển khai client OpenRouter dùng chung: 1 HttpClient tĩnh, BaseUrl + ApiKey từ OpenRouterOptions.
/// Luôn yêu cầu response_format = json_object. Tự retry khi 429/5xx. Timeout đặt theo từng lần gọi
/// (qua CancellationTokenSource), không giữ biến timeout riêng cho mỗi pipeline.
/// </summary>
public sealed class OpenRouterClient : IOpenRouterClient
{
    private static readonly HttpClient _http = new();
    private readonly OpenRouterOptions _opt;

    public OpenRouterClient(OpenRouterOptions opt) => _opt = opt;

    public async Task<string> ChatJsonAsync(
        string model,
        IReadOnlyList<object> content,
        int maxAttempts = 3,
        int? maxTokens = null,
        double? temperature = null,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? pdfParserEngine = null,
        bool enableRouterMetadata = false,
        int timeoutSeconds = 180,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opt.ApiKey))
            throw new Exception("Chưa cấu hình OpenRouter ApiKey (mục \"OpenRouter\" trong appsettings.json).");

        var url = _opt.BaseUrl.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[] { new Dictionary<string, object> { ["role"] = "user", ["content"] = content } },
            ["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" }
        };
        if (maxTokens.HasValue) body["max_tokens"] = maxTokens.Value;
        if (temperature.HasValue) body["temperature"] = temperature.Value;
        if (reasoningEnabled.HasValue)
            body["reasoning"] = new Dictionary<string, object> { ["enabled"] = reasoningEnabled.Value };
        if (!string.IsNullOrWhiteSpace(reasoningEffort))
            body["reasoning_effort"] = reasoningEffort.Trim();
        if (!string.IsNullOrWhiteSpace(pdfParserEngine))
        {
            body["plugins"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = "file-parser",
                    ["pdf"] = new Dictionary<string, string>
                    {
                        ["engine"] = pdfParserEngine.Trim()
                    }
                }
            };
        }
        var payload = JsonSerializer.Serialize(body);

        int attempts = Math.Max(1, maxAttempts);
        int delayMs = 2000;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutSeconds > 0) timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
                if (enableRouterMetadata)
                    req.Headers.TryAddWithoutValidation("X-OpenRouter-Metadata", "enabled");

                var res = await _http.SendAsync(req, timeoutCts.Token);
                var resStr = await res.Content.ReadAsStringAsync(timeoutCts.Token);
                if (enableRouterMetadata)
                    await SaveRouterMetadataAsync(model, resStr, timeoutCts.Token);
                if (res.IsSuccessStatusCode) return ExtractContent(resStr);

                bool transient = (int)res.StatusCode == 429 || ((int)res.StatusCode >= 500 && (int)res.StatusCode <= 599);
                if (transient && attempt < attempts)
                {
                    await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1), ct);
                    continue;
                }
                throw new Exception($"OpenRouter lỗi HTTP {(int)res.StatusCode}: {resStr}");
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested
                                       && (ex is HttpRequestException or TaskCanceledException or OperationCanceledException))
            {
                // Lỗi mạng hoặc quá hạn lần gọi này (ct chưa bị huỷ) → thử lại.
                await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
        }
        throw new Exception($"OpenRouter gọi thất bại sau {attempts} lần thử.");
    }

    /// <summary>Bóc nội dung text trong choices[0].message.content.</summary>
    private static string ExtractContent(string resStr)
    {
        using var doc = JsonDocument.Parse(resStr);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].GetProperty("message").TryGetProperty("content", out var c))
            return c.GetString() ?? "";
        return "";
    }

    private static async Task SaveRouterMetadataAsync(string model, string resStr, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(resStr);
            if (!doc.RootElement.TryGetProperty("openrouter_metadata", out var metadata))
                return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OCR WinApp");
            Directory.CreateDirectory(dir);

            var line =
                "{\"timestamp\":" + JsonSerializer.Serialize(DateTimeOffset.UtcNow.ToString("O")) +
                ",\"model\":" + JsonSerializer.Serialize(model) +
                ",\"openrouter_metadata\":" + metadata.GetRawText() +
                "}";
            await File.AppendAllTextAsync(Path.Combine(dir, "openrouter-metadata.jsonl"), line + Environment.NewLine, Encoding.UTF8, ct);
        }
        catch { /* Metadata is diagnostic-only. */ }
    }
}
