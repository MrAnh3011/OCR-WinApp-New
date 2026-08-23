namespace OCR.Business.Models;

/// <summary>
/// Build-time AI profile used by all AI pipelines.
/// Values are generated from ai-provider.local.json into SecretStore.
/// </summary>
public sealed class AiProviderOptions
{
    public string Provider { get; set; } = "google-ai-studio";
    public string Url { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-pro";
}
