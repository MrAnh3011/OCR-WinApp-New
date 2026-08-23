using System.IO;

namespace OCR.Business.Models;

/// <summary>
/// Cau hinh man OCR GCN iLis-UB. Man nay DUNG CHUNG prompt/schema/extract service/exporter Excel
/// va cache JSON voi man iLIS, nen muc nay CHI cau hinh worker, template Excel va file quy tac
/// gop/doi ten. KHONG co TempDir rieng: cache di qua NewGcnRunCacheService voi screenKey "gcn-ilis-ub".
/// </summary>
public sealed class GcnIlisUbOptions
{
    public int Workers { get; set; } = 5;

    /// <summary>Tien xu ly anh khong ho tro trong app nay, mac dinh gui file tho.</summary>
    public bool OptimizeImages { get; set; } = false;

    /// <summary>Duong dan Excel template, tuong doi thu muc exe.</summary>
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_FormMau_v3.xlsx");

    /// <summary>File JSON quy tac gop/doi ten file kem theo, tuong doi thu muc exe.</summary>
    public string RulesFile { get; set; } = "gcn-ilis-ub-rules.json";
}
