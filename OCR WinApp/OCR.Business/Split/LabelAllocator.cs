using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OCR.Business.Split;

/// <summary>
/// Cấp phát tên (label) thư mục con duy nhất theo serial GCN, an toàn đa luồng
/// (port từ allocate_label trong split_gcn.py). Nếu trùng → thêm -1, -2, ...
/// </summary>
public sealed class LabelAllocator
{
    private readonly string _baseDir;
    private readonly object _lock = new();
    private readonly HashSet<string> _used = new(System.StringComparer.OrdinalIgnoreCase);

    public LabelAllocator(string baseDir) => _baseDir = baseDir;

    /// <summary>Trả về label duy nhất và tạo thư mục tương ứng (không chép đè).</summary>
    public (string Label, string Dir) Allocate(string serial)
        => AllocateGroup(serial, 1)[0];

    /// <summary>
    /// Cấp phát một nhóm thư mục cho GCN nhiều thửa. Ví dụ serial A, parcelCount=3
    /// sẽ tạo A-1, A-2, A-3 và reserve cả label gốc A để tránh va chạm trong phiên chạy song song.
    /// </summary>
    public IReadOnlyList<(string Label, string Dir)> AllocateGroup(string serial, int parcelCount)
    {
        lock (_lock)
        {
            parcelCount = System.Math.Max(1, parcelCount);
            var baseLabel = serial;
            int suffix = 0;

            while (true)
            {
                var labels = parcelCount == 1
                    ? new[] { baseLabel }
                    : Enumerable.Range(1, parcelCount).Select(i => $"{baseLabel}-{i}").ToArray();

                var labelsToReserve = parcelCount == 1
                    ? labels
                    : labels.Prepend(baseLabel);

                bool hasConflict = labelsToReserve.Any(label =>
                    _used.Contains(label) || Directory.Exists(Path.Combine(_baseDir, label)));

                if (!hasConflict)
                {
                    foreach (var label in labelsToReserve)
                        _used.Add(label);

                    var result = labels
                        .Select(label =>
                        {
                            var dir = Path.Combine(_baseDir, label);
                            Directory.CreateDirectory(dir);
                            return (label, dir);
                        })
                        .ToList();

                    return result;
                }

                suffix++;
                baseLabel = $"{serial}-{suffix}";
            }
        }
    }
}
