using System;
using System.IO;
using System.Text.Json;
using OCR.Business.Auth;
using OCR.Business.Models;
using OCR.Business.Notifications;
using OCR.Business.Security;

namespace OCR.Business.Configuration;

/// <summary>
/// Đọc appsettings.business.json (cạnh exe — AppContext.BaseDirectory) + fallback SecretStore và dựng TẤT CẢ
/// Options nghiệp vụ (API/url/model/OCR/worker...). TẦNG NGHIỆP VỤ là nơi DUY NHẤT biết các thông tin này;
/// tầng App chỉ gọi các hàm dưới đây để lấy Options đăng ký DI, KHÔNG tự parse api/model/ocr.
/// (Cấu hình GIAO DIỆN nằm ở OCR WinApp/appsettings.json — mục Appearance.)
/// </summary>
public static class AppSettingsLoader
{
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.business.json");

    /// <summary>API đăng nhập (BaseUrl + timeout).</summary>
    public static AuthApiOptions LoadAuthApi()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AuthApiOptions();
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (!doc.RootElement.TryGetProperty("Api", out var api)) return new AuthApiOptions();

            return new AuthApiOptions
            {
                BaseUrl = api.TryGetProperty("BaseUrl", out var baseUrl) ? baseUrl.GetString() ?? "" : "",
                TimeoutSeconds = api.TryGetProperty("TimeoutSeconds", out var timeout) && timeout.TryGetInt32(out var seconds)
                    ? seconds : 30
            };
        }
        catch { return new AuthApiOptions(); }
    }

    /// <summary>OpenRouter dùng chung (BaseUrl + ApiKey, rỗng → SecretStore).</summary>
    public static OpenRouterOptions LoadOpenRouter()
    {
        var opt = new OpenRouterOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("OpenRouter", out var o))
                {
                    opt.ApiKey = Str(o, "ApiKey", opt.ApiKey);
                    opt.BaseUrl = Str(o, "BaseUrl", opt.BaseUrl);
                }
            }
        }
        catch { }
        if (string.IsNullOrEmpty(opt.ApiKey)) opt.ApiKey = SecretStore.OpenRouterApiKey;
        return opt;
    }

    /// <summary>AI provider/model dung chung cho moi man AI; build tu ai-provider.local.json vao SecretStore.</summary>
    public static AiProviderOptions LoadAiProvider()
    {
        return new AiProviderOptions
        {
            Provider = SecretStore.AiProvider,
            Url = SecretStore.AiProviderUrl,
            ApiKey = SecretStore.AiProviderApiKey,
            Model = SecretStore.AiProviderModel
        };
    }

    /// <summary>Đất Uỷ Ban (worker/retry/template; AI profile dung chung).</summary>
    public static UyBanOptions LoadUyBan()
    {
        var opt = new UyBanOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("UyBan", out var u))
                {
                    opt.TemplateExcel = Str(u, "TemplateExcel", opt.TemplateExcel);
                    opt.Workers = Int(u, "Workers", opt.Workers);
                    opt.MaxRetries = Int(u, "MaxRetries", opt.MaxRetries);
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>Tách GCN (worker/retry/timeout/output; AI profile dung chung).</summary>
    public static SplitGcnOptions LoadSplitGcn()
    {
        var opt = new SplitGcnOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("Split", out var s))
                {
                    opt.Workers = Int(s, "Workers", opt.Workers);
                    opt.MaxRetries = Int(s, "MaxRetries", opt.MaxRetries);
                    opt.RequestTimeoutSeconds = Int(s, "RequestTimeoutSeconds", opt.RequestTimeoutSeconds);
                    opt.ReasoningEffort = Str(s, "ReasoningEffort", opt.ReasoningEffort);
                    opt.PdfParserEngine = Str(s, "PdfParserEngine", opt.PdfParserEngine);
                    opt.EnableRouterMetadata = Bool(s, "EnableRouterMetadata", opt.EnableRouterMetadata);
                    opt.OutputSubFolder = Str(s, "OutputSubFolder", opt.OutputSubFolder);
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>GCN (New) worker/template; AI profile dung chung.</summary>
    public static NewGcnOptions LoadNewGcn()
    {
        var opt = new NewGcnOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("NewGcn", out var n))
                {
                    opt.TemplateExcel = Str(n, "TemplateExcel", opt.TemplateExcel);
                    opt.TempDir = Str(n, "TempDir", opt.TempDir);
                    opt.Workers = Int(n, "Workers", opt.Workers);
                    if (n.TryGetProperty("OptimizeImages", out var o) && (o.ValueKind == JsonValueKind.True || o.ValueKind == JsonValueKind.False))
                        opt.OptimizeImages = o.GetBoolean();
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>OCR GCN VietBD worker/template — muc cau hinh RIENG, khong dung chung voi man iLIS.</summary>
    public static VietBdGcnOptions LoadVietBdGcn()
    {
        var opt = new VietBdGcnOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("VietBdGcn", out var n))
                {
                    opt.TemplateExcel = Str(n, "TemplateExcel", opt.TemplateExcel);
                    opt.TempDir = Str(n, "TempDir", opt.TempDir);
                    opt.Workers = Int(n, "Workers", opt.Workers);
                    if (n.TryGetProperty("OptimizeImages", out var o) && (o.ValueKind == JsonValueKind.True || o.ValueKind == JsonValueKind.False))
                        opt.OptimizeImages = o.GetBoolean();
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>OCR GCN iLis-UB — worker/template/file quy tac. Dung chung cache voi man iLIS.</summary>
    public static GcnIlisUbOptions LoadIlisUbGcn()
    {
        var opt = new GcnIlisUbOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("IlisUbGcn", out var n))
                {
                    opt.TemplateExcel = Str(n, "TemplateExcel", opt.TemplateExcel);
                    opt.RulesFile = Str(n, "RulesFile", opt.RulesFile);
                    opt.Workers = Int(n, "Workers", opt.Workers);
                    if (n.TryGetProperty("OptimizeImages", out var o) && (o.ValueKind == JsonValueKind.True || o.ValueKind == JsonValueKind.False))
                        opt.OptimizeImages = o.GetBoolean();
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>Man "Xoa trang trang" — so worker + 4 nguong nhan dien trang trang.</summary>
    public static BlankPageOptions LoadBlankPage()
    {
        var opt = new BlankPageOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("BlankPage", out var b))
                {
                    opt.Workers = Int(b, "Workers", opt.Workers);
                    opt.RenderDpi = Int(b, "RenderDpi", opt.RenderDpi);
                    opt.CropMarginRatio = Dbl(b, "CropMarginRatio", opt.CropMarginRatio);
                    opt.DarkPixelThreshold = Int(b, "DarkPixelThreshold", opt.DarkPixelThreshold);
                    opt.InkRatioThreshold = Dbl(b, "InkRatioThreshold", opt.InkRatioThreshold);
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>Man "Doi ten theo Serial" — worker + tham so render/crop cho OCR offline.</summary>
    public static SerialRenameOptions LoadSerialRename()
    {
        var opt = new SerialRenameOptions();
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("SerialRename", out var s))
                {
                    opt.Workers = Int(s, "Workers", opt.Workers);
                    opt.RenderDpi = Int(s, "RenderDpi", opt.RenderDpi);
                    opt.CropRightRatio = Dbl(s, "CropRightRatio", opt.CropRightRatio);
                    opt.CropBottomRatio = Dbl(s, "CropBottomRatio", opt.CropBottomRatio);
                    opt.GcnKeyword = Str(s, "GcnKeyword", opt.GcnKeyword);
                    opt.OcrLanguage = Str(s, "OcrLanguage", opt.OcrLanguage);
                }
            }
        }
        catch { }
        return opt;
    }

    /// <summary>Cau hinh luong upload Gemini Files API dung chung.</summary>
    public static GeminiUploadOptions LoadGeminiUpload()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                return ParseGeminiUpload(doc.RootElement);
            }
        }
        catch { }
        return new GeminiUploadOptions();
    }

    /// <summary>Tach rieng de test duoc ma khong can file tren dia.</summary>
    internal static GeminiUploadOptions ParseGeminiUpload(JsonElement root)
    {
        var opt = new GeminiUploadOptions();
        if (!root.TryGetProperty("GeminiUpload", out var g)) return opt;

        opt.Workers = Int(g, "Workers", opt.Workers);
        opt.MaxRetries = Int(g, "MaxRetries", opt.MaxRetries);
        opt.RetryBaseDelayMs = Int(g, "RetryBaseDelayMs", opt.RetryBaseDelayMs);
        return opt;
    }

    /// <summary>Cau hinh bao cao nen sau khi export thanh cong.</summary>
    public static ExportNotificationOptions LoadExportNotification()
    {
        return new ExportNotificationOptions
        {
            Enabled = true,
            BaseUrl = SecretStore.ExportEndpoint,
            AccessToken = SecretStore.ExportAccessKey,
            RecipientId = SecretStore.ExportRecipient,
            TimeoutSeconds = 15
        };
    }

    private static string Str(JsonElement el, string name, string fallback)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static int Int(JsonElement el, string name, int fallback)
        => el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

    private static double Dbl(JsonElement el, string name, double fallback)
        => el.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;

    private static bool Bool(JsonElement el, string name, bool fallback)
        => el.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : fallback;
}
