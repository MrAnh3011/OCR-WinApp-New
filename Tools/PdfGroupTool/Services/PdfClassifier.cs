using System.Text.RegularExpressions;
using PdfGroupTool.Models;

namespace PdfGroupTool.Services;

public static class PdfClassifier
{
    // Hỗ trợ nhận diện:
    // {serial}-GCN, {serial}-GT, {serial}-GTK
    // Có hoặc không có khoảng trắng quanh dấu gạch ngang
    // Có ghi chú phụ sau loại file: {serial}-GCN <ghi chu>.pdf
    // Không phân biệt hoa thường
    private static readonly Regex StandardPattern = new(
        @"^(?<serial>.+?)\s*[-_]\s*(?<type>GCN|GTK|GT)(?:\b|[\s._(]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpacePattern = new(
        @"^(?<serial>.+?)\s+(?<type>GCN|GTK|GT)(?:\b|[\s._(]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string filePath, out PdfFileInfo? info)
    {
        info = null;
        if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = Path.GetFileName(filePath);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath).Trim();

        // 1. Thử chuẩn có dấu phân cách: {serial}-GCN / {serial}_GT...
        var match = StandardPattern.Match(nameWithoutExt);
        if (match.Success)
        {
            string serial = CleanSerial(match.Groups["serial"].Value);
            string docType = match.Groups["type"].Value.ToUpperInvariant();
            info = new PdfFileInfo
            {
                SourcePath = filePath,
                FileName = fileName,
                Serial = serial,
                DocType = docType
            };
            return true;
        }

        // 2. Thử phân cách bằng khoảng trắng: {serial} GCN...
        match = SpacePattern.Match(nameWithoutExt);
        if (match.Success)
        {
            string serial = CleanSerial(match.Groups["serial"].Value);
            string docType = match.Groups["type"].Value.ToUpperInvariant();
            info = new PdfFileInfo
            {
                SourcePath = filePath,
                FileName = fileName,
                Serial = serial,
                DocType = docType
            };
            return true;
        }

        // 3. Dự phòng nếu có dấu gạch ngang bất kỳ
        int lastDash = nameWithoutExt.LastIndexOf('-');
        if (lastDash > 0)
        {
            string serial = CleanSerial(nameWithoutExt.Substring(0, lastDash));
            string type = nameWithoutExt.Substring(lastDash + 1).Trim();
            if (!string.IsNullOrWhiteSpace(serial))
            {
                info = new PdfFileInfo
                {
                    SourcePath = filePath,
                    FileName = fileName,
                    Serial = serial,
                    DocType = type.ToUpperInvariant()
                };
                return true;
            }
        }

        return false;
    }

    public static string CleanSerial(string rawSerial)
    {
        string s = rawSerial.Trim();
        // Xóa các ký tự không hợp lệ cho tên thư mục Windows: \ / : * ? " < > |
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c, '_');
        }
        return s.Trim();
    }
}

