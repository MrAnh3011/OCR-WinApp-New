using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Models;

namespace OCR.Business.Ai;

/// <summary>
/// Provider-neutral client for JSON AI responses.
/// Supports Google AI Studio generateContent and OpenRouter chat/completions.
/// </summary>
public sealed class AiModelClient : IAiModelClient
{
    private readonly HttpClient _http;
    private readonly AiProviderOptions _opt;

    public AiModelClient(AiProviderOptions opt)
        : this(opt, null)
    {
    }

    public AiModelClient(AiProviderOptions opt, HttpMessageHandler? handler)
    {
        _opt = opt;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public Task<string> GenerateJsonAsync(
        IReadOnlyList<object> content,
        int maxAttempts = 3,
        int? maxTokens = null,
        double? temperature = null,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? pdfParserEngine = null,
        bool enableRouterMetadata = false,
        int timeoutSeconds = 180,
        object? responseSchema = null,
        string? escalatedReasoningEffort = null,
        CancellationToken ct = default)
    {
        var provider = (_opt.Provider ?? "").Trim().ToLowerInvariant();
        return provider switch
        {
            "google-ai-studio" or "google" or "gemini" or "ai-studio" => GenerateGoogleAiStudioJsonAsync(
                content, maxAttempts, maxTokens, temperature, reasoningEnabled, reasoningEffort,
                timeoutSeconds, responseSchema, escalatedReasoningEffort, ct),
            "openrouter" or "open-router" => GenerateOpenRouterJsonAsync(
                content, maxAttempts, maxTokens, temperature, reasoningEnabled, reasoningEffort,
                pdfParserEngine, enableRouterMetadata, timeoutSeconds, escalatedReasoningEffort, ct),
            _ => throw new Exception($"Provider AI khong duoc ho tro: '{_opt.Provider}'.")
        };
    }

    private async Task<string> GenerateGoogleAiStudioJsonAsync(
        IReadOnlyList<object> content,
        int maxAttempts,
        int? maxTokens,
        double? temperature,
        bool? reasoningEnabled,
        string? reasoningEffort,
        int timeoutSeconds,
        object? responseSchema,
        string? escalatedReasoningEffort,
        CancellationToken ct)
    {
        EnsureConfigured(_opt.Url, _opt.ApiKey, _opt.Model, "Google AI Studio");

        string BuildPayload(bool includeSchema, int attempt)
        {
            var generationConfig = new Dictionary<string, object>
            {
                ["responseMimeType"] = "application/json"
            };
            if (temperature.HasValue) generationConfig["temperature"] = temperature.Value;
            if (maxTokens.HasValue) generationConfig["maxOutputTokens"] = maxTokens.Value;
            // LỚP 2: ràng buộc cấu trúc đầu ra (constrained decoding) để model không đóng thừa ngoặc.
            if (includeSchema && responseSchema is not null) generationConfig["responseSchema"] = responseSchema;

            var thinkingConfig = BuildGoogleThinkingConfig(
                reasoningEnabled, EffortForAttempt(reasoningEffort, escalatedReasoningEffort, attempt));
            if (thinkingConfig is not null)
                generationConfig["thinkingConfig"] = thinkingConfig;

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["contents"] = new object[]
                {
                    new Dictionary<string, object> { ["parts"] = ToGeminiParts(content) }
                },
                ["generationConfig"] = generationConfig
            });
        }

        HttpRequestMessage RequestFactory() => new(HttpMethod.Post, BuildGoogleAiStudioUrl());

        try
        {
            return await SendAndExtractAsync(
                RequestFactory, attempt => BuildPayload(includeSchema: true, attempt),
                ExtractGeminiText, maxAttempts, timeoutSeconds, ct);
        }
        catch (Exception ex) when (responseSchema is not null && IsClientError(ex))
        {
            // Provider có thể từ chối responseSchema (400/404) → thử lại KHÔNG schema.
            // Lớp 1 (cân bằng ngoặc) + Lớp 3 (retry) vẫn bảo vệ kết quả, không tệ hơn trước.
            return await SendAndExtractAsync(
                RequestFactory, attempt => BuildPayload(includeSchema: false, attempt),
                ExtractGeminiText, maxAttempts, timeoutSeconds, ct);
        }
    }

    /// <summary>
    /// Mức reasoning cho một lượt gọi. Lượt đầu (attempt 1) luôn dùng mức gốc — phần lớn file ra đúng
    /// ngay nên không trả phí thinking cao vô ích. Từ lượt THỬ LẠI (attempt ≥ 2) trở đi mới nâng lên
    /// mức escalate, vì đã có bằng chứng lượt trước thất bại. Không truyền escalate ⇒ giữ nguyên mức gốc
    /// ở mọi lượt (hành vi cũ, dùng cho 3 màn Tách GCN + Đất Uỷ Ban).
    /// </summary>
    private static string? EffortForAttempt(string? baseEffort, string? escalatedEffort, int attempt)
        => attempt >= 2 && !string.IsNullOrWhiteSpace(escalatedEffort) ? escalatedEffort : baseEffort;

    private async Task<string> GenerateOpenRouterJsonAsync(
        IReadOnlyList<object> content,
        int maxAttempts,
        int? maxTokens,
        double? temperature,
        bool? reasoningEnabled,
        string? reasoningEffort,
        string? pdfParserEngine,
        bool enableRouterMetadata,
        int timeoutSeconds,
        string? escalatedReasoningEffort,
        CancellationToken ct)
    {
        EnsureConfigured(_opt.Url, _opt.ApiKey, _opt.Model, "OpenRouter");

        string BuildPayload(int attempt)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = _opt.Model,
                ["messages"] = new[]
                {
                    new Dictionary<string, object> { ["role"] = "user", ["content"] = content }
                },
                ["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" }
            };
            if (maxTokens.HasValue) body["max_tokens"] = maxTokens.Value;
            if (temperature.HasValue) body["temperature"] = temperature.Value;
            if (reasoningEnabled.HasValue)
                body["reasoning"] = new Dictionary<string, object> { ["enabled"] = reasoningEnabled.Value };
            var effort = EffortForAttempt(reasoningEffort, escalatedReasoningEffort, attempt);
            if (!string.IsNullOrWhiteSpace(effort))
                body["reasoning_effort"] = effort.Trim();
            if (!string.IsNullOrWhiteSpace(pdfParserEngine))
            {
                body["plugins"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "file-parser",
                        ["pdf"] = new Dictionary<string, string> { ["engine"] = pdfParserEngine.Trim() }
                    }
                };
            }

            return JsonSerializer.Serialize(body);
        }

        return await SendAndExtractAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildOpenRouterUrl());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
                if (enableRouterMetadata)
                    request.Headers.TryAddWithoutValidation("X-OpenRouter-Metadata", "enabled");
                return request;
            },
            BuildPayload,
            ExtractOpenRouterContent,
            maxAttempts,
            timeoutSeconds,
            ct,
            resStr => enableRouterMetadata ? SaveRouterMetadataAsync(_opt.Model, resStr, ct) : Task.CompletedTask);
    }

    /// <summary>
    /// Gửi request có retry, trích nội dung model, cân bằng ngoặc (Lớp 1) rồi kiểm tra JSON parse được.
    /// Nếu JSON không hợp lệ (ví dụ bị cắt cụt) và còn lượt thì gọi lại model (LỚP 3); trả về chuỗi JSON
    /// đã làm sạch, sẵn sàng cho caller parse. Retry cả lỗi HTTP tạm thời (429/5xx) lẫn lỗi parse.
    ///
    /// <paramref name="payloadFactory"/> được gọi LẠI cho từng lượt (nhận số lượt, đếm từ 1) chứ không
    /// dùng một payload dựng sẵn: nhờ vậy lượt thử lại có thể nâng mức reasoning (xem
    /// <see cref="EffortForAttempt"/>) thay vì lặp y nguyên request đã thất bại.
    /// </summary>
    private async Task<string> SendAndExtractAsync(
        Func<HttpRequestMessage> requestFactory,
        Func<int, string> payloadFactory,
        Func<string, string> extractContent,
        int maxAttempts,
        int timeoutSeconds,
        CancellationToken ct,
        Func<string, Task>? afterResponse = null)
    {
        var attempts = Math.Max(1, maxAttempts);
        const int delayMs = 2000;
        string lastCleaned = "";

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutSeconds > 0) timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var request = requestFactory();
                request.Content = new StringContent(payloadFactory(attempt), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, timeoutCts.Token);
                var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (afterResponse is not null)
                    await afterResponse(responseText);

                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(response.StatusCode) && attempt < attempts)
                    {
                        await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1), ct);
                        continue;
                    }

                    throw new Exception($"AI API loi HTTP {(int)response.StatusCode}: {responseText}");
                }

                lastCleaned = AiJsonText.QuoteInvalidLeadingZeroNumbers(
                    AiJsonText.MergeSplitDigitStrings(
                        AiJsonText.ExtractBalancedJson(extractContent(responseText))));
                if (AiJsonText.IsParsableJson(lastCleaned))
                    return lastCleaned;

                // JSON không hợp lệ (thường do bị cắt cụt/thiếu ngoặc) → gọi lại nếu còn lượt.
                if (attempt < attempts)
                    await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
        }

        throw new Exception(
            $"AI API tra ve JSON khong hop le sau {attempts} lan thu ({lastCleaned.Length} ky tu). " +
            $"Doan nhan duoc: {SnippetForError(lastCleaned)}");
    }

    /// <summary>Rút gọn nội dung để đưa vào thông báo lỗi (đầu + cuối) giúp thấy JSON bị cắt ở đâu.</summary>
    private static string SnippetForError(string text)
    {
        if (string.IsNullOrEmpty(text)) return "(rong)";
        const int head = 200, tail = 80;
        return text.Length <= head + tail + 20
            ? text
            : $"{text[..head]} … {text[^tail..]}";
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => (int)statusCode == 429 || ((int)statusCode >= 500 && (int)statusCode <= 599);

    /// <summary>Lỗi HTTP phía client (4xx) — dùng để quyết định thử lại KHÔNG kèm responseSchema.</summary>
    private static bool IsClientError(Exception ex)
        => ex.Message.Contains("loi HTTP 4", StringComparison.Ordinal);

    private Dictionary<string, object>? BuildGoogleThinkingConfig(bool? reasoningEnabled, string? reasoningEffort)
    {
        var effort = (reasoningEffort ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(effort))
            return reasoningEnabled == true
                ? new Dictionary<string, object> { ["thinkingBudget"] = -1 }
                : null;

        if (effort is "off" or "none" or "false" or "disabled")
            return new Dictionary<string, object> { ["thinkingBudget"] = 0 };

        return IsGemini3OrLater(_opt.Model)
            ? new Dictionary<string, object> { ["thinkingLevel"] = MapGoogleThinkingLevel(effort) }
            : new Dictionary<string, object> { ["thinkingBudget"] = MapGoogleThinkingBudget(effort) };
    }

    private static bool IsGemini3OrLater(string model)
    {
        var normalized = model.Trim().ToLowerInvariant();
        if (normalized.StartsWith("models/", StringComparison.Ordinal))
            normalized = normalized["models/".Length..];

        return normalized.StartsWith("gemini-3", StringComparison.Ordinal);
    }

    private static string MapGoogleThinkingLevel(string effort)
        => effort switch
        {
            "minimal" or "min" => "MINIMAL",
            "low" => "LOW",
            "medium" or "med" => "MEDIUM",
            "high" => "HIGH",
            _ => "MEDIUM"
        };

    private static int MapGoogleThinkingBudget(string effort)
        => effort switch
        {
            "minimal" or "min" => 512,
            "low" => 1024,
            "medium" or "med" => 4096,
            "high" => 8192,
            "dynamic" or "auto" => -1,
            _ => 4096
        };

    private string BuildGoogleAiStudioUrl()
    {
        var url = _opt.Url.Trim();
        if (url.EndsWith(":generateContent", StringComparison.OrdinalIgnoreCase))
            return AddApiKeyQuery(url);

        var modelPath = _opt.Model.Trim().StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? _opt.Model.Trim()
            : "models/" + _opt.Model.Trim();
        return AddApiKeyQuery($"{url.TrimEnd('/')}/{modelPath}:generateContent");
    }

    private string AddApiKeyQuery(string url)
    {
        if (url.Contains("key=", StringComparison.OrdinalIgnoreCase))
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return url + separator + "key=" + Uri.EscapeDataString(_opt.ApiKey);
    }

    private string BuildOpenRouterUrl()
    {
        var url = _opt.Url.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";
        return url;
    }

    private static object[] ToGeminiParts(IReadOnlyList<object> content)
    {
        var parts = new List<object>();
        foreach (var item in content)
        {
            if (!TryAsDictionary(item, out var dict) || !dict.TryGetValue("type", out var typeValue))
                continue;

            var type = Convert.ToString(typeValue)?.Trim().ToLowerInvariant();
            switch (type)
            {
                case "text":
                    if (dict.TryGetValue("text", out var textValue))
                        parts.Add(new Dictionary<string, object> { ["text"] = Convert.ToString(textValue) ?? "" });
                    break;

                case "image_url":
                    if (TryReadDataUrl(dict, "image_url", "url", out var imageMime, out var imageData))
                        parts.Add(ToInlineDataPart(imageMime, imageData));
                    break;

                case "file":
                    if (TryReadDataUrl(dict, "file", "file_data", out var fileMime, out var fileData))
                        parts.Add(ToInlineDataPart(fileMime, fileData));
                    break;

                case "file_uri":
                    if (TryReadFileUri(dict, out var uriMime, out var fileUri))
                    {
                        parts.Add(new Dictionary<string, object>
                        {
                            ["file_data"] = new Dictionary<string, string>
                            {
                                ["mime_type"] = uriMime,
                                ["file_uri"] = fileUri
                            }
                        });
                    }
                    break;
            }
        }

        if (parts.Count == 0)
            throw new Exception("Noi dung AI khong co prompt hoac file/anh hop le.");

        return parts.ToArray();
    }

    private static Dictionary<string, object> ToInlineDataPart(string mimeType, string data)
        => new()
        {
            ["inline_data"] = new Dictionary<string, string>
            {
                ["mime_type"] = mimeType,
                ["data"] = data
            }
        };

    private static bool TryReadDataUrl(
        IReadOnlyDictionary<string, object> root,
        string containerName,
        string valueName,
        out string mimeType,
        out string data)
    {
        mimeType = "";
        data = "";
        if (!root.TryGetValue(containerName, out var container) || !TryAsDictionary(container, out var dict))
            return false;
        if (!dict.TryGetValue(valueName, out var value))
            return false;

        return TryParseDataUrl(Convert.ToString(value) ?? "", out mimeType, out data);
    }

    private static bool TryReadFileUri(
        IReadOnlyDictionary<string, object> root,
        out string mimeType,
        out string fileUri)
    {
        mimeType = "";
        fileUri = "";
        if (!root.TryGetValue("file_uri", out var container) || !TryAsDictionary(container, out var dict))
            return false;
        if (!dict.TryGetValue("mime_type", out var mimeValue) ||
            !dict.TryGetValue("file_uri", out var uriValue))
            return false;

        mimeType = Convert.ToString(mimeValue) ?? "";
        fileUri = Convert.ToString(uriValue) ?? "";
        return !string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(fileUri);
    }

    private static bool TryParseDataUrl(string value, out string mimeType, out string data)
    {
        mimeType = "";
        data = "";
        const string marker = ";base64,";
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        mimeType = value.Substring("data:".Length, markerIndex - "data:".Length);
        data = value[(markerIndex + marker.Length)..];
        return !string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(data);
    }

    private static bool TryAsDictionary(object value, out IReadOnlyDictionary<string, object> dict)
    {
        if (value is IReadOnlyDictionary<string, object> readOnly)
        {
            dict = readOnly;
            return true;
        }

        if (value is IDictionary<string, object> normal)
        {
            dict = new Dictionary<string, object>(normal, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        if (value is IReadOnlyDictionary<string, string> stringDict)
        {
            dict = stringDict.ToDictionary(kv => kv.Key, kv => (object)kv.Value, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        dict = new Dictionary<string, object>();
        return false;
    }

    private static string ExtractOpenRouterContent(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.GetRawText();
        }

        throw new Exception("Khong tim thay noi dung phan hoi tu OpenRouter.");
    }

    private static string ExtractGeminiText(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            // GHÉP TẤT CẢ part văn bản (bỏ qua part "thought"): Gemini có thể chia JSON ra nhiều part,
            // hoặc kèm part suy nghĩ ở đầu — lấy 1 part đầu sẽ thiếu/nhầm khiến JSON không parse được.
            var sb = new StringBuilder();
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                        continue;
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString());
                }
            }

            if (sb.Length > 0)
                return sb.ToString();

            // Không có văn bản trả lời → nêu rõ lý do để chẩn đoán (thường MAX_TOKENS / SAFETY / RECITATION).
            var reason = candidate.TryGetProperty("finishReason", out var fr) && fr.ValueKind == JsonValueKind.String
                ? fr.GetString()
                : "khong ro";
            throw new Exception(
                $"Google AI Studio khong tra ve van ban JSON (finishReason={reason}). " +
                "Thuong do output bi cat vi MAX_TOKENS hoac bi chan an toan; xem lai file nguon hoac tang maxOutputTokens.");
        }

        if (root.TryGetProperty("promptFeedback", out var feedback) &&
            feedback.TryGetProperty("blockReason", out var block) && block.ValueKind == JsonValueKind.String)
            throw new Exception($"Google AI Studio chan yeu cau (blockReason={block.GetString()}).");

        throw new Exception("Khong tim thay noi dung phan hoi tu Google AI Studio.");
    }

    private static void EnsureConfigured(string url, string apiKey, string model, string providerName)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new Exception($"Chua cau hinh Url cho {providerName}.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception($"Chua cau hinh ApiKey cho {providerName}.");
        if (string.IsNullOrWhiteSpace(model))
            throw new Exception($"Chua cau hinh Model cho {providerName}.");
    }

    private static async Task SaveRouterMetadataAsync(string model, string responseText, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
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
        catch
        {
            // Metadata is diagnostic-only.
        }
    }
}
