using System.Collections.Generic;
using OCR.Business.Models;

namespace OCR.Business.UyBan;

/// <summary>Ghi danh sách UyBanRecord ra Excel theo template Excel_FormMau_v5.xlsx (sheet "Data").</summary>
public interface IUyBanExcelExporter
{
    void Write(IEnumerable<UyBanRecord> records, string outputPath, string templatePath);
}
