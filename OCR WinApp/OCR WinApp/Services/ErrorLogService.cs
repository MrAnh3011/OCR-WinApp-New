using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OCR_WinApp.Services;

public sealed class ErrorLogService : IErrorLogService
{
    private static readonly object SyncRoot = new();

    public void LogException(string screenKey, string source, Exception exception)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OCR WinApp",
                "error-log");
            Directory.CreateDirectory(logDir);

            var fileName = "error-" + SanitizeScreenKey(screenKey) + ".log";
            var logPath = Path.Combine(logDir, fileName);
            var entry = BuildEntry(source, exception);

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
        }
        catch (Exception logException)
        {
            Debug.WriteLine("Ghi error log that bai: " + logException.Message);
        }
    }

    private static string BuildEntry(string source, Exception exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine("============================================================");
        builder.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.AppendLine("Source: " + source);
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        return builder.ToString();
    }

    private static string SanitizeScreenKey(string screenKey)
    {
        var value = string.IsNullOrWhiteSpace(screenKey) ? "app" : screenKey.Trim().ToLowerInvariant();
        value = Regex.Replace(value, @"[<>:""/\\|?*\x00-\x1F]+", "-");
        value = Regex.Replace(value, @"\s+", "-");
        value = Regex.Replace(value, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(value) ? "app" : value;
    }
}
