using System.IO;

namespace OCR.Business.Models;

/// <summary>
/// Cau hinh man "Convert docx to Excel VBD" (muc "DocxVbd" trong appsettings.business.json).
/// Man nay parse docx thuan code — KHONG dung AI, KHONG worker/upload/cache, nen chi can template.
/// Template DUNG CHUNG voi man OCR GCN VietBD (cung khuon KeKhaiDangKy 161 cot).
/// </summary>
public sealed class DocxVbdOptions
{
    public string TemplateExcel { get; set; } = Path.Combine("Assets", "Temp", "Excel_Template_VietBD.xlsx");
}
