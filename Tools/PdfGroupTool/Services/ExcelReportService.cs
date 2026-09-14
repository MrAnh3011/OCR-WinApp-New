using ClosedXML.Excel;
using PdfGroupTool.Models;

namespace PdfGroupTool.Services;

public class ExcelReportService
{
    public string GenerateReport(List<PdfGroup> groups, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string reportFileName = $"TongHop_KetQua_{timestamp}.xlsx";
        string reportFilePath = Path.Combine(outputDirectory, reportFileName);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tổng hợp hồ sơ");

        // Thiết lập tiêu đề cột
        // Cột A: STT
        // Cột B: Tên file
        // Cột C: Hyperlink (Đường dẫn file)
        ws.Cell(1, 1).Value = "STT";
        ws.Cell(1, 2).Value = "Tên file";
        ws.Cell(1, 3).Value = "Đường dẫn file (Hyperlink)";

        // Style cho Header
        var headerRange = ws.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78"); // Tông xanh đậm chuyên nghiệp
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 28;

        int currentRow = 2;
        int stt = 1;

        foreach (var group in groups)
        {
            int groupFileCount = group.Files.Count;
            if (groupFileCount == 0) continue;

            // Mỗi nhóm serial chiếm ĐÚNG MỘT dòng: GCN/GT/GTK gộp chung, ngăn nhau bằng dấu phẩy.
            // Vì vậy không còn phải merge ô STT như trước.
            int row = currentRow;

            var sttCell = ws.Cell(row, 1);
            sttCell.Value = stt;
            sttCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sttCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // Cột B: tên toàn bộ file trong nhóm, giữ đúng thứ tự GCN → GT → GTK do FileGrouperService sắp.
            var nameCell = ws.Cell(row, 2);
            nameCell.Value = string.Join(", ", group.Files.Select(f => f.FileName));
            nameCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // Cột C: đường dẫn của các file, cùng thứ tự và cũng ngăn bằng dấu phẩy.
            // Excel chỉ cho phép MỘT hyperlink trên mỗi ô, nên nhóm nhiều file thì link trỏ vào thư mục
            // chứa cả nhóm (bấm một phát mở ra đủ GCN/GT/GTK); nhóm một file vẫn link thẳng tới file đó.
            var linkCell = ws.Cell(row, 3);
            string displayPaths = string.Join(", ",
                group.Files.Select(f => f.RelativePath ?? f.DestinationPath ?? f.FileName));
            string linkTarget = groupFileCount == 1
                ? (group.Files[0].RelativePath ?? group.Files[0].DestinationPath ?? group.Files[0].FileName)
                : Path.Combine(".", group.FolderName);

            // Dùng công thức HYPERLINK để link còn chạy đúng khi copy cả thư mục sang máy khác.
            linkCell.FormulaA1 =
                $"=HYPERLINK(\"{EscapeForFormula(linkTarget)}\", \"{EscapeForFormula(displayPaths)}\")";
            linkCell.Style.Font.FontColor = XLColor.FromHtml("#0563C1");
            linkCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            linkCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ws.Row(row).Height = 22;

            currentRow = row + 1;
            stt++;
        }

        // Định dạng toàn bộ bảng
        int lastDataRow = Math.Max(2, currentRow - 1);
        var dataRange = ws.Range(1, 1, lastDataRow, 3);
        dataRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.TopBorderColor = XLColor.FromHtml("#D9D9D9");
        dataRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#D9D9D9");
        dataRange.Style.Border.LeftBorderColor = XLColor.FromHtml("#D9D9D9");
        dataRange.Style.Border.RightBorderColor = XLColor.FromHtml("#D9D9D9");

        // Header border đậm hơn một chút
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        headerRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1F4E78");

        // Tự động căn chỉnh độ rộng cột
        ws.Columns(1, 3).AdjustToContents();
        // Cột STT tối thiểu rộng 8
        if (ws.Column(1).Width < 8) ws.Column(1).Width = 8;
        // Cột Tên file và Đường dẫn cộng thêm lề
        ws.Column(2).Width += 4;
        ws.Column(3).Width += 6;
        // Gộp 3 file vào một ô làm nội dung dài gấp ba, để AdjustToContents tự do thì cột rộng đến mức
        // không đọc nổi — chặn trần lại cho bảng vừa màn hình.
        if (ws.Column(2).Width > 70) ws.Column(2).Width = 70;
        if (ws.Column(3).Width > 90) ws.Column(3).Width = 90;

        workbook.SaveAs(reportFilePath);
        return reportFilePath;
    }

    /// <summary>Nhân đôi dấu nháy kép để chuỗi nằm an toàn trong công thức HYPERLINK.</summary>
    private static string EscapeForFormula(string value) => value.Replace("\"", "\"\"");
}
