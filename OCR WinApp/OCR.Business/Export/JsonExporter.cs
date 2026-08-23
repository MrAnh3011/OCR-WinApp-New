using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OCR.Business.Models;

namespace OCR.Business.Export;

/// <summary>Ghi kết quả OCR ra file JSON có cấu trúc (text + dòng + bounding box).</summary>
public sealed class JsonExporter : IResultExporter
{
    public string Extension => ".json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // giữ nguyên tiếng Việt có dấu
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task ExportAsync(OcrResult result, string outputPath, CancellationToken ct = default)
    {
        await using var fs = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(fs, result, Options, ct);
    }
}
