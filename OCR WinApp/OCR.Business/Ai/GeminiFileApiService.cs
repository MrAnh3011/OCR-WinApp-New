using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OCR.Business.Models;

namespace OCR.Business.Ai;

public sealed class GeminiFileApiService : IGeminiFileApiService
{
    private const string ActiveState = "ACTIVE";
    private const string FailedState = "FAILED";

    /// <summary>Hạn thời gian cho request nhẹ (khởi tạo upload, kiểm tra trạng thái, xóa file).</summary>
    private static readonly TimeSpan MetadataRequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Hạn thời gian cho request đẩy nội dung file lên (file lớn cần lâu hơn).</summary>
    private static readonly TimeSpan UploadRequestTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly AiProviderOptions _options;
    private readonly string _cachePath;
    private readonly TimeSpan _pollInterval;
    private readonly string _profileId;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _uploadGates = new(StringComparer.Ordinal);
    private Dictionary<string, GeminiFileCacheEntry> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    public GeminiFileApiService(AiProviderOptions options)
        : this(options, null, null)
    {
    }

    internal GeminiFileApiService(
        AiProviderOptions options,
        HttpMessageHandler? handler,
        string? cachePath,
        TimeSpan? pollInterval = null)
    {
        _options = options;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OCR WinApp",
            "gemini-files.json");
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _profileId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{options.Url}|{options.ApiKey}")));
    }

    public bool IsEnabled
    {
        get
        {
            var provider = (_options.Provider ?? "").Trim().ToLowerInvariant();
            return provider is "google-ai-studio" or "google" or "gemini" or "ai-studio";
        }
    }

    public async Task PrepareExecutionAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        await _stateGate.WaitAsync(ct);
        try
        {
            _entries = await LoadCacheAsync(ct);
            _loaded = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<GeminiFileReference> GetOrUploadAsync(
        string sourcePath,
        string artifactKey,
        string displayName,
        string mimeType,
        byte[] content,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Provider hiện tại không hỗ trợ Gemini Files API.");
        if (content.Length == 0)
            throw new InvalidOperationException("File gửi Gemini không có dữ liệu.");

        await EnsureLoadedAsync(ct);

        var normalizedSourcePath = NormalizeSourcePath(sourcePath);
        var contentHash = Convert.ToHexString(SHA256.HashData(content));
        var cacheKey = BuildCacheKey(normalizedSourcePath, artifactKey, contentHash);
        var uploadGate = _uploadGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await uploadGate.WaitAsync(ct);
        try
        {
            var cached = await FindEntryAsync(cacheKey, ct);
            if (cached is not null)
            {
                var current = await GetFileAsync(cached.Name, ct);
                if (current is not null)
                {
                    current = await WaitUntilReadyAsync(current, ct);
                    if (string.Equals(current.State, ActiveState, StringComparison.OrdinalIgnoreCase))
                    {
                        cached.Name = current.Name;
                        cached.Uri = current.Uri;
                        cached.MimeType = current.MimeType;
                        cached.State = current.State;
                        await UpsertAndSaveAsync(cached, ct);
                        return new GeminiFileReference(cached.Name, cached.Uri, cached.MimeType);
                    }
                }
            }

            var uploaded = await UploadAsync(displayName, mimeType, content, ct);
            var entry = CreateEntry(
                cacheKey, normalizedSourcePath, artifactKey, contentHash, displayName, uploaded);
            // Ghi ngay name/uri sau upload để vẫn tái sử dụng được nếu polling hoặc inference lỗi.
            await UpsertAndSaveAsync(entry, ct);

            uploaded = await WaitUntilReadyAsync(uploaded, ct);
            if (!string.Equals(uploaded.State, ActiveState, StringComparison.OrdinalIgnoreCase))
            {
                var failedEntry = CreateEntry(
                    cacheKey, normalizedSourcePath, artifactKey, contentHash, displayName, uploaded);
                await UpsertAndSaveAsync(failedEntry, ct);
                throw new Exception(BuildProcessingError(uploaded));
            }

            entry = CreateEntry(
                cacheKey, normalizedSourcePath, artifactKey, contentHash, displayName, uploaded);
            await UpsertAndSaveAsync(entry, ct);
            return new GeminiFileReference(entry.Name, entry.Uri, entry.MimeType);
        }
        finally
        {
            uploadGate.Release();
        }
    }

    public async Task DeleteBySourcePathsAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        await EnsureLoadedAsync(ct);
        var normalizedPaths = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeSourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedPaths.Count == 0) return;

        List<GeminiFileCacheEntry> targets;
        await _stateGate.WaitAsync(ct);
        try
        {
            targets = _entries.Values
                .Where(entry => string.Equals(entry.ProfileId, _profileId, StringComparison.Ordinal) &&
                                normalizedPaths.Contains(entry.SourcePath))
                .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }
        finally
        {
            _stateGate.Release();
        }

        foreach (var target in targets)
        {
            await DeleteRemoteFileAsync(target.Name, ct);

            await _stateGate.WaitAsync(ct);
            try
            {
                foreach (var key in _entries
                    .Where(pair => string.Equals(pair.Value.Name, target.Name, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    _entries.Remove(key);
                }
                await SaveCacheUnsafeAsync(ct);
            }
            finally
            {
                _stateGate.Release();
            }
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await PrepareExecutionAsync(ct);
    }

    private async Task<GeminiFileCacheEntry?> FindEntryAsync(string cacheKey, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            return _entries.TryGetValue(cacheKey, out var entry) ? entry : null;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task UpsertAndSaveAsync(GeminiFileCacheEntry entry, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            _entries[entry.Key] = entry;
            await SaveCacheUnsafeAsync(ct);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// Gửi request kèm hạn thời gian riêng cho từng lần gọi. <see cref="_http"/> đặt
    /// <see cref="Timeout.InfiniteTimeSpan"/> nên nếu không chặn ở đây, một kết nối bị treo sẽ giữ task
    /// chạy mãi và màn hình OCR không bao giờ kết thúc (tiến độ đứng, Export không bật được).
    /// Quá hạn ném <see cref="TimeoutException"/> để phân biệt với việc người dùng bấm dừng.
    /// </summary>
    private async Task<(HttpResponseMessage Response, string Body)> SendWithTimeoutAsync(
        HttpRequestMessage request,
        TimeSpan timeout,
        string step,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return (response, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            response?.Dispose();
            throw new TimeoutException($"Gemini Files API {step} quá {timeout.TotalSeconds:0} giây.");
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    private async Task<GeminiFileResource> UploadAsync(
        string displayName,
        string mimeType,
        byte[] content,
        CancellationToken ct)
    {
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, BuildUploadStartUrl());
        AddApiKeyHeader(startRequest);
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "start");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Length", content.Length.ToString());
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Type", mimeType);
        startRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                file = new { display_name = LimitDisplayName(displayName) }
            }),
            Encoding.UTF8,
            "application/json");

        var start = await SendWithTimeoutAsync(startRequest, MetadataRequestTimeout, "khởi tạo upload", ct);
        using var startResponse = start.Response;
        var startBody = start.Body;
        if (!startResponse.IsSuccessStatusCode)
            throw new Exception($"Gemini Files API upload start lỗi HTTP {(int)startResponse.StatusCode}: {startBody}");
        if (!startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var uploadUrls))
            throw new Exception("Gemini Files API không trả về URL upload.");

        var uploadUrl = uploadUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uploadUrl))
            throw new Exception("Gemini Files API trả về URL upload rỗng.");

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Offset", "0");
        uploadRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "upload, finalize");
        uploadRequest.Content = new ByteArrayContent(content);
        uploadRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        uploadRequest.Content.Headers.ContentLength = content.Length;

        var upload = await SendWithTimeoutAsync(uploadRequest, UploadRequestTimeout, "tải file lên", ct);
        using var uploadResponse = upload.Response;
        var uploadBody = upload.Body;
        if (!uploadResponse.IsSuccessStatusCode)
            throw new Exception($"Gemini Files API upload lỗi HTTP {(int)uploadResponse.StatusCode}: {uploadBody}");

        return ParseFileResource(uploadBody);
    }

    private async Task<GeminiFileResource?> GetFileAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildFileUrl(name));
        AddApiKeyHeader(request);
        var sent = await SendWithTimeoutAsync(request, MetadataRequestTimeout, "kiểm tra trạng thái file", ct);
        using var response = sent.Response;
        var body = sent.Body;
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Gemini Files API kiểm tra file lỗi HTTP {(int)response.StatusCode}: {body}");
        return ParseFileResource(body);
    }

    private async Task<GeminiFileResource> WaitUntilReadyAsync(
        GeminiFileResource file,
        CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        while (!string.Equals(file.State, ActiveState, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(file.State, FailedState, StringComparison.OrdinalIgnoreCase))
        {
            if (DateTime.UtcNow - startedAt > TimeSpan.FromMinutes(10))
                throw new TimeoutException("Gemini Files API xử lý file quá 10 phút.");

            await Task.Delay(_pollInterval, ct);
            file = await GetFileAsync(file.Name, ct)
                ?? throw new Exception("File đã biến mất khỏi Gemini Files API trong khi đang xử lý.");
        }

        return file;
    }

    private async Task DeleteRemoteFileAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildFileUrl(name));
        AddApiKeyHeader(request);
        var sent = await SendWithTimeoutAsync(request, MetadataRequestTimeout, "xóa file", ct);
        using var response = sent.Response;
        var body = sent.Body;
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        throw new Exception($"Gemini Files API xóa file lỗi HTTP {(int)response.StatusCode}: {body}");
    }

    private async Task<Dictionary<string, GeminiFileCacheEntry>> LoadCacheAsync(CancellationToken ct)
    {
        if (!File.Exists(_cachePath))
            return new Dictionary<string, GeminiFileCacheEntry>(StringComparer.Ordinal);

        var json = await File.ReadAllTextAsync(_cachePath, ct);
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, GeminiFileCacheEntry>(StringComparer.Ordinal);

        var document = JsonSerializer.Deserialize<GeminiFileCacheDocument>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new GeminiFileCacheDocument();

        return document.Files
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) &&
                            !string.IsNullOrWhiteSpace(entry.Name) &&
                            !string.IsNullOrWhiteSpace(entry.Uri))
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    private async Task SaveCacheUnsafeAsync(CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = _cachePath + ".tmp";
        var document = new GeminiFileCacheDocument
        {
            Files = _entries.Values
                .OrderBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ArtifactKey, StringComparer.Ordinal)
                .ToList()
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct);
        File.Move(tempPath, _cachePath, overwrite: true);
    }

    private string BuildUploadStartUrl() => BuildApiRoot() + "/upload/v1beta/files";

    private string BuildFileUrl(string name)
    {
        var normalizedName = name.TrimStart('/');
        return BuildApiRoot() + "/v1beta/" + normalizedName;
    }

    private string BuildApiRoot()
    {
        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var uri))
            throw new Exception("URL Google AI Studio không hợp lệ.");

        var path = uri.AbsolutePath;
        var versionIndex = path.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
        var prefix = versionIndex >= 0 ? path[..versionIndex] : path.TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority}{prefix}".TrimEnd('/');
    }

    private void AddApiKeyHeader(HttpRequestMessage request)
        => request.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);

    private static GeminiFileResource ParseFileResource(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (root.TryGetProperty("file", out var nested))
            root = nested;

        var file = new GeminiFileResource
        {
            Name = ReadString(root, "name"),
            Uri = ReadString(root, "uri"),
            MimeType = ReadString(root, "mimeType"),
            State = ReadString(root, "state")
        };
        if (root.TryGetProperty("error", out var error))
            file.Error = error.GetRawText();

        if (string.IsNullOrWhiteSpace(file.Name) || string.IsNullOrWhiteSpace(file.Uri))
            throw new Exception("Gemini Files API trả về thiếu name hoặc uri.");
        return file;
    }

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private GeminiFileCacheEntry CreateEntry(
        string key,
        string sourcePath,
        string artifactKey,
        string contentHash,
        string displayName,
        GeminiFileResource file)
        => new()
        {
            Key = key,
            ProfileId = _profileId,
            SourcePath = sourcePath,
            ArtifactKey = artifactKey,
            ContentSha256 = contentHash,
            DisplayName = displayName,
            Name = file.Name,
            Uri = file.Uri,
            MimeType = file.MimeType,
            State = file.State,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static string BuildProcessingError(GeminiFileResource file)
        => string.IsNullOrWhiteSpace(file.Error)
            ? $"Gemini Files API xử lý file thất bại (state={file.State})."
            : $"Gemini Files API xử lý file thất bại: {file.Error}";

    private string BuildCacheKey(string sourcePath, string artifactKey, string contentHash)
        => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{_profileId}|{sourcePath}|{artifactKey}|{contentHash}")));

    private static string NormalizeSourcePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return path.Trim(); }
    }

    private static string LimitDisplayName(string displayName)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? "document" : displayName.Trim();
        return value.Length <= 512 ? value : value[..512];
    }

    private sealed class GeminiFileCacheDocument
    {
        public List<GeminiFileCacheEntry> Files { get; set; } = new();
    }

    private sealed class GeminiFileCacheEntry
    {
        public string Key { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string ArtifactKey { get; set; } = "";
        public string ContentSha256 { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Uri { get; set; } = "";
        public string MimeType { get; set; } = "";
        public string State { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class GeminiFileResource
    {
        public string Name { get; set; } = "";
        public string Uri { get; set; } = "";
        public string MimeType { get; set; } = "";
        public string State { get; set; } = "";
        public string Error { get; set; } = "";
    }
}
