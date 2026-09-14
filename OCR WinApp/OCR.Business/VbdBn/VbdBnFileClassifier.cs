using System;
using OCR.Business.IlisUb;

namespace OCR.Business.VbdBn;

public enum VbdBnFileKind { None, Gcn, Gtk }

/// <summary>
/// Phân loại file theo TÊN (không kể phần mở rộng): chứa GcnKeyword → lô GCN; chứa GtkKeyword và
/// KHÔNG chứa GcnKeyword → lô GTK. So khớp bỏ dấu + không phân biệt hoa thường — dùng chung phép
/// chuẩn hoá của màn iLis-UB qua <see cref="GcnFolderRules.Normalize"/> (hàm public static).
/// </summary>
public static class VbdBnFileClassifier
{
    public static VbdBnFileKind Classify(string fileNameWithoutExtension, string gcnKeyword, string gtkKeyword)
    {
        var name = GcnFolderRules.Normalize(fileNameWithoutExtension);
        if (name.Length == 0) return VbdBnFileKind.None;

        var gcn = GcnFolderRules.Normalize(gcnKeyword);
        var gtk = GcnFolderRules.Normalize(gtkKeyword);

        if (gcn.Length > 0 && name.Contains(gcn, StringComparison.Ordinal)) return VbdBnFileKind.Gcn;
        if (gtk.Length > 0 && name.Contains(gtk, StringComparison.Ordinal)) return VbdBnFileKind.Gtk;
        return VbdBnFileKind.None;
    }
}
