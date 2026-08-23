using System;
using System.IO;

namespace OCR.Business.Models;

/// <summary>
/// Cau hinh pipeline OCR GCN New: gui file len LLM, nhan envelope JSON va xuat Excel form day du.
/// </summary>
public sealed class NewGcnOptions
{
    public int Workers { get; set; } = 2;

    /// <summary>Tien xu ly anh OpenCV khong ho tro trong app nay, mac dinh gui file tho.</summary>
    public bool OptimizeImages { get; set; } = false;

    /// <summary>Thu muc xuat Excel mac dinh.</summary>
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "OCR WinApp", "Output");

    /// <summary>Thu muc tam/cache JSON cua man OCR GCN New.</summary>
    public string TempDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "newgcn-temp");

    /// <summary>Duong dan Excel template, tuong doi thu muc exe.</summary>
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_FormMau_v3.xlsx");
}
