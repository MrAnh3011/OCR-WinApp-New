using System.Runtime.InteropServices;
using PdfGroupTool.Services;

namespace PdfGroupTool;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Kiểm tra chế độ CLI (dòng lệnh)
        if (args.Length >= 2 && args.Any(a => a.Equals("--cli", StringComparison.OrdinalIgnoreCase) || a.Equals("-c", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            RunCliMode(args);
            return;
        }

        string? initialInput = args.Length > 0 ? args[0] : null;
        string? initialOutput = args.Length > 1 ? args[1] : null;

        Application.Run(new MainForm(initialInput, initialOutput));
    }

    private static void RunCliMode(string[] args)
    {
        var nonFlagArgs = args.Where(a => !a.StartsWith("-")).ToList();
        if (nonFlagArgs.Count < 2)
        {
            Console.WriteLine("Cú pháp CLI: GomFilePdfTool.exe <Thư mục Input> <Thư mục Output> [--cli]");
            return;
        }

        string inputDir = nonFlagArgs[0];
        string outputDir = nonFlagArgs[1];

        Console.WriteLine($"[CLI] Đang quét thư mục: {inputDir}");
        var grouper = new FileGrouperService();
        var excel = new ExcelReportService();

        try
        {
            var scan = grouper.ScanAndGroup(inputDir, recursive: true);
            Console.WriteLine($"[CLI] Tìm thấy {scan.TotalPdfsFound} file PDF, nhận diện {scan.Groups.Count} bộ ({scan.Groups.Sum(g => g.FileCount)} file).");

            if (scan.Groups.Count == 0)
            {
                Console.WriteLine("[CLI] Không có file nào cần gom.");
                return;
            }

            grouper.ProcessCopyAsync(scan.Groups, outputDir, null, msg => Console.WriteLine(msg), CancellationToken.None).GetAwaiter().GetResult();
            string excelPath = excel.GenerateReport(scan.Groups, outputDir);
            Console.WriteLine($"[CLI] Hoàn tất! File báo cáo: {excelPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI LỖI] {ex.Message}");
        }
    }
}
