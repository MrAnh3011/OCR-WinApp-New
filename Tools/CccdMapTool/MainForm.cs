using System.Diagnostics;
using CccdMapTool.Models;
using CccdMapTool.Services;

namespace CccdMapTool;

public partial class MainForm : Form
{
    private TextBox txtInput = null!;
    private TextBox txtOutput = null!;
    private Button btnBrowseInput = null!;
    private Button btnBrowseOutput = null!;
    private CheckBox chkOpenExcel = null!;
    private Button btnStart = null!;
    private Label lblStatus = null!;
    private TextBox txtLog = null!;

    private readonly CccdMapService _service = new();

    public MainForm(string? defaultInput = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(defaultInput))
            SetInput(defaultInput);
    }

    private void InitializeComponent()
    {
        this.Text = "Công cụ Map thông tin CCCD sang Kê Khai Đăng Ký - GEOAIOT";
        this.Size = new Size(820, 600);
        this.MinimumSize = new Size(750, 520);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        this.BackColor = Color.FromArgb(248, 249, 250);
        this.AllowDrop = true;
        this.DragEnter += MainForm_DragEnter;
        this.DragDrop += MainForm_DragDrop;

        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(31, 78, 120),
            Padding = new Padding(20, 12, 20, 10)
        };

        pnlHeader.Controls.Add(new Label
        {
            Text = "MAP THÔNG TIN CCCD → SHEET KÊ KHAI ĐĂNG KÝ",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12)
        });

        pnlHeader.Controls.Add(new Label
        {
            Text = "Điền thông tin còn thiếu ở KeKhaiDangKy từ sheet ThongTinCCCD (file kết quả màn OCR GCN VBD-BN)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(220, 230, 242),
            AutoSize = true,
            Location = new Point(20, 38)
        });

        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 15, 20, 15),
            AutoScroll = true
        };

        var grpFiles = new GroupBox
        {
            Text = " File Excel ",
            Location = new Point(20, 10),
            Size = new Size(760, 115),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        grpFiles.Controls.Add(new Label
        {
            Text = "File đầu vào (.xlsx):",
            Location = new Point(15, 30),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        });

        txtInput = new TextBox
        {
            Location = new Point(180, 27),
            Size = new Size(450, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        btnBrowseInput = new Button
        {
            Text = "Chọn...",
            Location = new Point(640, 26),
            Size = new Size(100, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.White,
            UseVisualStyleBackColor = true
        };
        btnBrowseInput.Click += (s, e) => BrowseInputFile();

        grpFiles.Controls.Add(new Label
        {
            Text = "File kết quả:",
            Location = new Point(15, 70),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        });

        txtOutput = new TextBox
        {
            Location = new Point(180, 67),
            Size = new Size(450, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        btnBrowseOutput = new Button
        {
            Text = "Chọn...",
            Location = new Point(640, 66),
            Size = new Size(100, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.White,
            UseVisualStyleBackColor = true
        };
        btnBrowseOutput.Click += (s, e) => BrowseOutputFile();

        grpFiles.Controls.Add(txtInput);
        grpFiles.Controls.Add(btnBrowseInput);
        grpFiles.Controls.Add(txtOutput);
        grpFiles.Controls.Add(btnBrowseOutput);

        var grpOptions = new GroupBox
        {
            Text = " Tùy chọn ",
            Location = new Point(20, 135),
            Size = new Size(760, 55),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        chkOpenExcel = new CheckBox
        {
            Text = "Mở file Excel kết quả khi xong",
            Location = new Point(20, 22),
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };
        grpOptions.Controls.Add(chkOpenExcel);

        btnStart = new Button
        {
            Text = "▶  BẮT ĐẦU MAP THÔNG TIN CCCD",
            Location = new Point(20, 200),
            Size = new Size(760, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(31, 78, 120),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnStart.FlatAppearance.BorderSize = 0;
        btnStart.Click += async (s, e) => await StartProcessingAsync();

        lblStatus = new Label
        {
            Text = "Sẵn sàng. Chọn file Excel kết quả của màn OCR GCN VBD-BN (có sheet ThongTinCCCD).",
            Location = new Point(20, 252),
            Size = new Size(760, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Italic, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(80, 80, 80)
        };

        var lblLogTitle = new Label
        {
            Text = "Nhật ký xử lý chi tiết:",
            Location = new Point(20, 278),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        txtLog = new TextBox
        {
            Location = new Point(20, 300),
            Size = new Size(760, 190),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        pnlBody.Controls.Add(grpFiles);
        pnlBody.Controls.Add(grpOptions);
        pnlBody.Controls.Add(btnStart);
        pnlBody.Controls.Add(lblStatus);
        pnlBody.Controls.Add(lblLogTitle);
        pnlBody.Controls.Add(txtLog);

        this.Controls.Add(pnlBody);
        this.Controls.Add(pnlHeader);
    }

    private void SetInput(string path)
    {
        txtInput.Text = path;
        if (txtOutput.Text.Trim().Length == 0)
            txtOutput.Text = Program.SuggestOutputPath(path);
    }

    private void BrowseInputFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Chọn file Excel kết quả (có sheet KeKhaiDangKy và ThongTinCCCD)",
            Filter = "Excel (*.xlsx)|*.xlsx|Tất cả (*.*)|*.*"
        };
        if (File.Exists(txtInput.Text)) ofd.FileName = txtInput.Text;
        if (ofd.ShowDialog() == DialogResult.OK) SetInput(ofd.FileName);
    }

    private void BrowseOutputFile()
    {
        using var sfd = new SaveFileDialog
        {
            Title = "Chọn nơi lưu file kết quả",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = txtOutput.Text.Trim().Length > 0
                ? Path.GetFileName(txtOutput.Text)
                : "ketqua-mapCCCD.xlsx"
        };
        if (sfd.ShowDialog() == DialogResult.OK) txtOutput.Text = sfd.FileName;
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
        => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && File.Exists(paths[0]))
            SetInput(paths[0]);
    }

    private async Task StartProcessingAsync()
    {
        string input = txtInput.Text.Trim();
        if (input.Length == 0 || !File.Exists(input))
        {
            MessageBox.Show("Chưa chọn file Excel đầu vào hợp lệ.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string output = txtOutput.Text.Trim();
        if (output.Length == 0)
        {
            output = Program.SuggestOutputPath(input);
            txtOutput.Text = output;
        }

        btnStart.Enabled = false;
        txtLog.Clear();
        lblStatus.Text = "Đang xử lý...";
        Log($"Bắt đầu: {Path.GetFileName(input)}");

        try
        {
            MapResult result = await Task.Run(() => _service.Run(input, output, Log));

            Log("");
            Log($"Dòng KeKhaiDangKy đã quét : {result.RowsScanned}");
            Log($"Khối chủ/vợ chồng map được: {result.BlocksMatched}");
            Log($"Số ô đã điền thêm         : {result.FieldsFilled}");
            Log($"Dòng có cảnh báo (cột FI) : {result.RowsWarned}");
            Log($"Bản ghi ThongTinCCCD      : {result.CccdTotal} (không dùng tới: {result.CccdUnused})");
            Log($"→ Đã lưu: {result.OutputPath}");

            lblStatus.Text = $"Xong. Điền thêm {result.FieldsFilled} ô, {result.RowsWarned} dòng có cảnh báo.";

            if (chkOpenExcel.Checked)
                Process.Start(new ProcessStartInfo(result.OutputPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"LỖI: {ex.Message}");
            lblStatus.Text = "Có lỗi xảy ra — xem nhật ký bên dưới.";
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnStart.Enabled = true;
        }
    }

    private void Log(string message)
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(() => Log(message));
            return;
        }
        txtLog.AppendText(message + Environment.NewLine);
    }
}
