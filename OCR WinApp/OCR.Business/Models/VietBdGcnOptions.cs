using System;
using System.IO;

namespace OCR.Business.Models;

/// <summary>
/// Cau hinh pipeline OCR GCN VietBD. Tach hoan toan khoi <see cref="NewGcnOptions"/> cua man iLIS:
/// doi Workers/template ben nay khong duoc anh huong man kia. Chi AI provider va luong upload Gemini
/// la dung chung theo cau truc chung cua he thong.
/// </summary>
public sealed class VietBdGcnOptions
{
    public int Workers { get; set; } = 5;

    /// <summary>Tien xu ly anh khong ho tro trong app nay, mac dinh gui file tho.</summary>
    public bool OptimizeImages { get; set; } = false;

    /// <summary>Thu muc xuat Excel mac dinh.</summary>
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "OCR WinApp", "Output");

    /// <summary>Thu muc tam/cache JSON RIENG cua man VietBD (khong dung chung newgcn-temp voi man iLIS).</summary>
    public string TempDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "vietbdgcn-temp");

    /// <summary>Duong dan Excel template cua Viet Ban Do, tuong doi thu muc exe.</summary>
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_Template_VietBD.xlsx");
}
