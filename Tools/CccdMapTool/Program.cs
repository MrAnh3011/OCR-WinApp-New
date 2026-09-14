using System.Runtime.InteropServices;
using System.Text;
using CccdMapTool.Services;

namespace CccdMapTool;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Chế độ dòng lệnh: MapCccdTool.exe <file input> [file output] --cli
        if (args.Length >= 1 && args.Any(a => a.Equals("--cli", StringComparison.OrdinalIgnoreCase)
                                              || a.Equals("-c", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            // Console kế thừa từ tiến trình cha có thể là code page 437/1258 → log tiếng Việt bị vỡ dấu.
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { /* không có console thật */ }
            RunCliMode(args);
            return;
        }

        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }

    private static void RunCliMode(string[] args)
    {
        var files = args.Where(a => !a.StartsWith('-')).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("Cú pháp CLI: MapCccdTool.exe <file Excel đầu vào> [file Excel kết quả] --cli");
            return;
        }

        string input = files[0];
        string output = files.Count > 1 ? files[1] : SuggestOutputPath(input);

        try
        {
            var result = new CccdMapService().Run(input, output, Console.WriteLine);
            Console.WriteLine();
            Console.WriteLine($"HOÀN TẤT → {result.OutputPath}");
            Console.WriteLine($"  Dòng KeKhaiDangKy đã quét : {result.RowsScanned}");
            Console.WriteLine($"  Khối chủ/vợ chồng map được: {result.BlocksMatched}");
            Console.WriteLine($"  Số ô đã điền thêm         : {result.FieldsFilled}");
            Console.WriteLine($"  Dòng có cảnh báo (cột FI) : {result.RowsWarned}");
            Console.WriteLine($"  Bản ghi ThongTinCCCD      : {result.CccdTotal} (không dùng tới: {result.CccdUnused})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LỖI: {ex.Message}");
        }
    }

    /// <summary>Tên file kết quả mặc định: cùng thư mục, thêm hậu tố mapCCCD + dấu thời gian.</summary>
    public static string SuggestOutputPath(string inputPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"{name}-mapCCCD_{stamp}.xlsx");
    }
}
