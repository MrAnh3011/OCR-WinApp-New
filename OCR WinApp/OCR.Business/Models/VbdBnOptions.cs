using System;
using System.IO;

namespace OCR.Business.Models;

/// <summary>
/// Cau hinh man OCR GCN VBD-BN. Lo GCN tai dung nguyen pipeline VietBD (prompt/schema/extract);
/// lo GTK co pipeline CCCD rieng. Tach khoi <see cref="VietBdGcnOptions"/>: doi Workers/template
/// ben nay khong duoc anh huong man VietBD goc.
/// </summary>
public sealed class VbdBnOptions
{
    public int Workers { get; set; } = 5;

    /// <summary>Tien xu ly anh khong ho tro, mac dinh gui file tho (nhu VietBD).</summary>
    public bool OptimizeImages { get; set; } = false;

    /// <summary>Thu muc xuat Excel mac dinh.</summary>
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "OCR WinApp", "Output");

    /// <summary>Goc cache JSON RIENG cua man VBD-BN (khong dung chung vietbdgcn-temp voi man VietBD).</summary>
    public string TempDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "vbdbn-temp");

    /// <summary>Template rieng: clone template VietBD + sheet ThongTinCCCD.</summary>
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_Template_VietBD_BN.xlsx");

    /// <summary>Ten file (bo duoi, bo dau, khong phan biet hoa thuong) chua cum nay → lo GCN.</summary>
    public string GcnKeyword { get; set; } = "GCN";

    /// <summary>Ten file chua cum nay (va KHONG chua GcnKeyword) → lo GTK (trich CCCD/CMND).</summary>
    public string GtkKeyword { get; set; } = "GTK";
}
