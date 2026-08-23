using ClosedXML.Excel;
using OCR.Business.Models;

namespace OCR.Business.Export;

/// <summary>Ghi kết quả OCR ra file Excel: sheet "Kết quả" (theo dòng) + sheet "Toàn văn".</summary>
public sealed class ExcelExporter : IResultExporter
{
    public string Extension => ".xlsx";

    public Task ExportAsync(OcrResult result, string outputPath, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        var ws = workbook.Worksheets.Add("Kết quả");
        ws.Cell(1, 1).Value = "Trang";
        ws.Cell(1, 2).Value = "Dòng";
        ws.Cell(1, 3).Value = "Nội dung";
        ws.Cell(1, 4).Value = "Độ tin cậy";
        ws.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var page in result.Pages)
        {
            var lineNo = 1;
            foreach (var line in page.Lines)
            {
                ws.Cell(row, 1).Value = page.PageNumber;
                ws.Cell(row, 2).Value = lineNo++;
                ws.Cell(row, 3).Value = line.Text;
                ws.Cell(row, 4).Value = line.Confidence;
                row++;
            }
        }

        ws.Column(1).Width = 8;
        ws.Column(2).Width = 8;
        ws.Column(3).Width = 80;
        ws.Column(4).Width = 12;

        var full = workbook.Worksheets.Add("Toàn văn");
        full.Cell(1, 1).Value = result.FullText;
        full.Column(1).Width = 100;
        full.Cell(1, 1).Style.Alignment.WrapText = true;
        full.Cell(1, 1).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);

        workbook.SaveAs(outputPath);
        return Task.CompletedTask;
    }
}
