using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace OCR.Business.Security;

/// <summary>
/// Hardware fingerprint từ CPU + Mainboard + Disk (port từ GcnOcrApp).
/// Dùng làm entropy cho DPAPI để dữ liệu chỉ giải mã được trên đúng máy.
/// </summary>
public static class HardwareIdHelper
{
    private static string? _cachedId;

    public static string GetHardwareId()
    {
        if (_cachedId != null) return _cachedId;

        var parts = new List<string>();
        Collect(parts, "SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        Collect(parts, "SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
        Collect(parts, "SELECT SerialNumber FROM Win32_DiskDrive", "SerialNumber");

        var raw = string.Join("|", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        _cachedId = Convert.ToHexString(hash)[..16];
        return _cachedId;
    }

    private static void Collect(List<string> parts, string query, string column)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
                parts.Add(obj[column]?.ToString()?.Trim() ?? "");
        }
        catch { /* WMI có thể bị chặn; bỏ qua phần này */ }
    }
}
