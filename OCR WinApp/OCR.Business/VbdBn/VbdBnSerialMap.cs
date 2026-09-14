using System;
using System.Collections.Generic;
using System.IO;

namespace OCR.Business.VbdBn;

/// <summary>
/// Gắn số serial GCN cho từng file GTK theo quy tắc đã chốt: serial của (các) GCN OCR thành công
/// nằm CÙNG THƯ MỤC CHA TRỰC TIẾP với file GTK. Nhiều GCN → nối "; " + cảnh báo;
/// không có GCN/serial → trống + cảnh báo. Hàm thuần, không đụng đĩa — unit-test được.
/// </summary>
public static class VbdBnSerialMap
{
    public static IReadOnlyDictionary<string, List<string>> Build(
        IEnumerable<(string GcnFilePath, string? Serial)> successfulGcns)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, serial) in successfulGcns)
        {
            if (string.IsNullOrWhiteSpace(serial)) continue;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(dir)) continue;
            if (!map.TryGetValue(dir, out var list)) map[dir] = list = new List<string>();
            var s = serial.Trim();
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }
        return map;
    }

    public static (string SerialJoined, string? Warning) Resolve(
        string gtkFilePath, IReadOnlyDictionary<string, List<string>> map)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(gtkFilePath)) ?? "";
        if (!map.TryGetValue(dir, out var serials) || serials.Count == 0)
            return ("", "Không tìm thấy GCN đọc được serial trong cùng thư mục hồ sơ — đối chiếu tay theo cột File nguồn.");

        if (serials.Count == 1) return (serials[0], null);

        return (string.Join("; ", serials),
            $"Thư mục hồ sơ có {serials.Count} GCN — không xác định được CCCD thuộc GCN nào, ghi mọi serial.");
    }
}
