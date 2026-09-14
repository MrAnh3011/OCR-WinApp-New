using System.Diagnostics;
using PdfGroupTool.Models;
using PdfGroupTool.Services;

namespace PdfGroupTool;

public partial class MainForm : Form
{
    private TextBox txtInput = null!;
    private TextBox txtOutput = null!;
    private Button btnBrowseInput = null!;
    private Button btnBrowseOutput = null!;
    private CheckBox chkRecursive = null!;
    private CheckBox chkOpenFolder = null!;
    private CheckBox chkOpenExcel = null!;
    private Button btnStart = null!;
    private Button btnCancel = null!;
    private ProgressBar progressBar = null!;
    private Label lblStatus = null!;
    private TextBox txtLog = null!;

    private CancellationTokenSource? _cts;
    private readonly FileGrouperService _grouperService = new();
    private readonly ExcelReportService _excelService = new();

    public MainForm(string? defaultInput = null, string? defaultOutput = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(defaultInput))
            txtInput.Text = defaultInput;
        if (!string.IsNullOrWhiteSpace(defaultOutput))
            txtOutput.Text = defaultOutput;
    }

    private void InitializeComponent()
    {
        this.Text = "Công cụ Gom File PDF GCN/GT & Xuất Báo Cáo Excel - GEOAIOT";
        this.Size = new Size(820, 620);
        this.MinimumSize = new Size(750, 550);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        this.BackColor = Color.FromArgb(248, 249, 250);
        this.AllowDrop = true;
        this.DragEnter += MainForm_DragEnter;
        this.DragDrop += MainForm_DragDrop;

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(31, 78, 120),
            Padding = new Padding(20, 12, 20, 10)
        };

        var lblTitle = new Label
        {
            Text = "CÔNG CỤ GOM FILE PDF GCN / GT & XUẤT BÁO CÁO EXCEL",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12)
        };

        var lblSubTitle = new Label
        {
            Text = "Tự động phân nhóm {serial}, gom file GCN/GT/GTK vào thư mục con và xuất Excel có Hyperlink",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(220, 230, 242),
            AutoSize = true,
            Location = new Point(20, 38)
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubTitle);

        // Body Container
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 15, 20, 15),
            AutoScroll = true
        };

        // GroupBox Thư mục
        var grpFolders = new GroupBox
        {
            Text = " Thư mục làm việc ",
            Location = new Point(20, 10),
            Size = new Size(760, 115),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        var lblInput = new Label
        {
            Text = "Thư mục đầu vào (Input):",
            Location = new Point(15, 30),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };

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
        btnBrowseInput.Click += (s, e) => BrowseInputFolder();

        var lblOutput = new Label
        {
            Text = "Thư mục kết quả (Output):",
            Location = new Point(15, 70),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };

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
        btnBrowseOutput.Click += (s, e) => BrowseOutputFolder();

        grpFolders.Controls.Add(lblInput);
        grpFolders.Controls.Add(txtInput);
        grpFolders.Controls.Add(btnBrowseInput);
        grpFolders.Controls.Add(lblOutput);
        grpFolders.Controls.Add(txtOutput);
        grpFolders.Controls.Add(btnBrowseOutput);

        // GroupBox Tuỳ chọn
        var grpOptions = new GroupBox
        {
            Text = " Tùy chọn ",
            Location = new Point(20, 135),
            Size = new Size(760, 55),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        chkRecursive = new CheckBox
        {
            Text = "Quét cả thư mục con (Recursive)",
            Location = new Point(20, 22),
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };

        chkOpenFolder = new CheckBox
        {
            Text = "Mở thư mục kết quả khi xong",
            Location = new Point(280, 22),
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };

        chkOpenExcel = new CheckBox
        {
            Text = "Mở file Excel khi xong",
            Location = new Point(520, 22),
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.Black
        };

        grpOptions.Controls.Add(chkRecursive);
        grpOptions.Controls.Add(chkOpenFolder);
        grpOptions.Controls.Add(chkOpenExcel);

        // Action Buttons
        btnStart = new Button
        {
            Text = "▶  BẮT ĐẦU GOM FILE & XUẤT EXCEL",
            Location = new Point(20, 200),
            Size = new Size(590, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(31, 78, 120),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnStart.FlatAppearance.BorderSize = 0;
        btnStart.Click += async (s, e) => await StartProcessingAsync();

        btnCancel = new Button
        {
            Text = "■  HỦY BỎ",
            Location = new Point(620, 200),
            Size = new Size(160, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => CancelProcessing();

        // Progress Section
        progressBar = new ProgressBar
        {
            Location = new Point(20, 252),
            Size = new Size(760, 18),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Style = ProgressBarStyle.Continuous
        };

        lblStatus = new Label
        {
            Text = "Sẵn sàng. Vui lòng chọn thư mục đầu vào và thư mục kết quả.",
            Location = new Point(20, 275),
            Size = new Size(760, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Italic, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(80, 80, 80)
        };

        // Log Box
        var lblLogTitle = new Label
        {
            Text = "Nhật ký xử lý chi tiết:",
            Location = new Point(20, 300),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(31, 78, 120)
        };

        txtLog = new TextBox
        {
            Location = new Point(20, 322),
            Size = new Size(760, 170),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        pnlBody.Controls.Add(grpFolders);
        pnlBody.Controls.Add(grpOptions);
        pnlBody.Controls.Add(btnStart);
        pnlBody.Controls.Add(btnCancel);
        pnlBody.Controls.Add(progressBar);
        pnlBody.Controls.Add(lblStatus);
        pnlBody.Controls.Add(lblLogTitle);
        pnlBody.Controls.Add(txtLog);

        this.Controls.Add(pnlBody);
        this.Controls.Add(pnlHeader);
    }

    private void BrowseInputFolder()
    {
        using var fbd = new FolderBrowserDialog();
        fbd.Description = "Chọn thư mục chứa các file PDF cần gom (Input)";
        if (!string.IsNullOrWhiteSpace(txtInput.Text) && Directory.Exists(txtInput.Text))
            fbd.SelectedPath = txtInput.Text;

        if (fbd.ShowDialog() == DialogResult.OK)
        {
            txtInput.Text = fbd.SelectedPath;
            if (string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                txtOutput.Text = Path.Combine(fbd.SelectedPath, "Output_GomFile");
            }
        }
    }

    private void BrowseOutputFolder()
    {
        using var fbd = new FolderBrowserDialog();
        fbd.Description = "Chọn thư mục lưu kết quả gom file và file Excel (Output)";
        if (!string.IsNullOrWhiteSpace(txtOutput.Text) && Directory.Exists(txtOutput.Text))
            fbd.SelectedPath = txtOutput.Text;

        if (fbd.ShowDialog() == DialogResult.OK)
        {
            txtOutput.Text = fbd.SelectedPath;
        }
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            string dropped = files[0];
            if (Directory.Exists(dropped))
            {
                txtInput.Text = dropped;
                if (string.IsNullOrWhiteSpace(txtOutput.Text))
                {
                    txtOutput.Text = Path.Combine(dropped, "Output_GomFile");
                }
            }
        }
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => Log(message)));
            return;
        }

        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        txtLog.AppendText(line + Environment.NewLine);
    }

    private async Task StartProcessingAsync()
    {
        string inputDir = txtInput.Text.Trim();
        string outputDir = txtOutput.Text.Trim();

        if (string.IsNullOrWhiteSpace(inputDir) || !Directory.Exists(inputDir))
        {
            MessageBox.Show("Vui lòng chọn thư mục đầu vào hợp lệ!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            MessageBox.Show("Vui lòng chọn thư mục kết quả!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Khóa UI
        btnStart.Enabled = false;
        btnCancel.Enabled = true;
        btnBrowseInput.Enabled = false;
        btnBrowseOutput.Enabled = false;
        txtInput.Enabled = false;
        txtOutput.Enabled = false;
        chkRecursive.Enabled = false;

        progressBar.Value = 0;
        lblStatus.Text = "Đang quét danh sách file PDF...";
        txtLog.Clear();
        Log("Bắt đầu tiến trình quét và gom file PDF...");
        Log($"Thư mục nguồn: {inputDir}");
        Log($"Thư mục đích:  {outputDir}");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // Bước 1: Quét và phân loại file
            var scanResult = await Task.Run(() => _grouperService.ScanAndGroup(inputDir, chkRecursive.Checked), ct);

            Log($"Tìm thấy tổng cộng: {scanResult.TotalPdfsFound} file PDF.");
            Log($"Nhận diện được: {scanResult.Groups.Count} bộ hồ sơ ({scanResult.Groups.Sum(g => g.FileCount)} file hợp lệ).");

            if (scanResult.UnmatchedFiles.Count > 0)
            {
                Log($"[Cảnh báo] Có {scanResult.UnmatchedFiles.Count} file không đúng định dạng {{serial}}-GCN / {{serial}}-GT:");
                foreach (var un in scanResult.UnmatchedFiles.Take(10))
                {
                    Log($"   - {Path.GetFileName(un)}");
                }
                if (scanResult.UnmatchedFiles.Count > 10)
                {
                    Log($"   ... và {scanResult.UnmatchedFiles.Count - 10} file khác.");
                }
            }

            if (scanResult.Groups.Count == 0)
            {
                MessageBox.Show("Không tìm thấy file PDF nào khớp định dạng {serial}-GCN / {serial}-GT trong thư mục đã chọn.",
                    "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Bước 2: Copy file vào các thư mục {serial}
            progressBar.Maximum = scanResult.Groups.Count;
            progressBar.Value = 0;

            var progress = new Progress<ProcessProgressReport>(report =>
            {
                if (report.ProcessedGroups <= progressBar.Maximum)
                    progressBar.Value = report.ProcessedGroups;
                lblStatus.Text = report.CurrentMessage;
            });

            await _grouperService.ProcessCopyAsync(scanResult.Groups, outputDir, progress, Log, ct);

            // Bước 3: Xuất báo cáo Excel
            lblStatus.Text = "Đang tạo file Excel tổng hợp kết quả...";
            Log("Đang khởi tạo file Excel tổng hợp...");

            string excelPath = await Task.Run(() => _excelService.GenerateReport(scanResult.Groups, outputDir), ct);

            Log($"Đã xuất file Excel thành công: {Path.GetFileName(excelPath)}");
            progressBar.Value = progressBar.Maximum;
            lblStatus.Text = "Hoàn thành xuất sắc!";

            MessageBox.Show($"Đã gom thành công {scanResult.Groups.Count} bộ hồ sơ ({scanResult.Groups.Sum(g => g.FileCount)} file)!\nFile Excel tổng hợp: {Path.GetFileName(excelPath)}",
                "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Tự động mở thư mục / file Excel nếu được chọn
            if (chkOpenFolder.Checked && Directory.Exists(outputDir))
            {
                try { Process.Start(new ProcessStartInfo { FileName = outputDir, UseShellExecute = true }); } catch { }
            }

            if (chkOpenExcel.Checked && File.Exists(excelPath))
            {
                try { Process.Start(new ProcessStartInfo { FileName = excelPath, UseShellExecute = true }); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            Log("[Đã hủy] Người dùng đã nhấn HỦY BỎ tiến trình.");
            lblStatus.Text = "Tiến trình đã bị dừng lại.";
            MessageBox.Show("Tiến trình đã bị hủy theo yêu cầu.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log($"[Lỗi nghiêm trọng] {ex.Message}");
            lblStatus.Text = "Có lỗi xảy ra trong quá trình xử lý.";
            MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi xử lý", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            btnBrowseInput.Enabled = true;
            btnBrowseOutput.Enabled = true;
            txtInput.Enabled = true;
            txtOutput.Enabled = true;
            chkRecursive.Enabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CancelProcessing()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            btnCancel.Enabled = false;
            lblStatus.Text = "Đang dừng lại...";
            _cts.Cancel();
        }
    }
}
