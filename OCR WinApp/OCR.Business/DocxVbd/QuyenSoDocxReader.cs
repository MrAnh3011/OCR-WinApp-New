using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OCR.Business.IlisUb;

namespace OCR.Business.DocxVbd;

/// <summary>
/// Đọc file .docx "Sổ cấp Giấy chứng nhận" (mẫu Q1 An Lạc) thành danh sách <see cref="QuyenSoDocxPage"/>
/// bằng THUẦN CODE — <see cref="ZipArchive"/> + <see cref="XDocument"/> có sẵn trong .NET, không NuGet
/// mới, không gọi API OCR.
///
/// Cấu trúc file: mỗi trang sổ = một bảng <c>w:tbl</c> cấp cao nhất trong <c>word/document.xml</c>;
/// đoạn văn ngay trước bảng in "Trang số: NN". Bên trong bảng, các dòng lần lượt: header
/// "I - NGƯỜI SỬ DỤNG ĐẤT" → dòng thông tin người (1 ô gộp) → "II - THỬA ĐẤT" → 3 dòng header cột →
/// các dòng dữ liệu thửa (11 ô) → "III - NHỮNG THAY ĐỔI..." → header 3 cột → các dòng biến động.
///
/// ⚠️ Văn bản trong docx có thể bị TÁCH RUN GIỮA TỪ (VD "NG Ư ỜI SỬ DỤNG Đ ẤT") nên mọi so khớp
/// header đều đi qua <see cref="Key"/>: bỏ dấu (<see cref="GcnFolderRules.Normalize"/>) + bỏ toàn bộ
/// ký tự không phải chữ/số. Bảng không có đủ marker mục I + mục II thì KHÔNG phải trang sổ → bỏ qua.
/// </summary>
public static class QuyenSoDocxReader
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly Regex TrangSoPattern = new(@"Trang\s*số\s*:?\s*(\S+)", RegexOptions.Compiled);

    /// <summary>
    /// Nhãn của mục I tách bằng regex trên văn bản đã gộp khoảng trắng. Nhãn "1.1."/"2.1." không neo
    /// theo chữ "Số CMTND..." nguyên văn (dễ lệch dấu) mà theo dạng "n.1. ... :".
    /// </summary>
    private static readonly Regex NguoiSuDungPattern = new(
        @"1\.\s*Họ\s*và\s*tên\s*:(?<n1>.*?)Năm\s*sinh\s*:(?<b1>.*?)1\.1\.[^:]*:(?<id1>.*?)1\.2\.[^:]*:(?<a1>.*?)" +
        @"(?:2\.\s*Họ\s*và\s*tên\s*:(?<n2>.*?)Năm\s*sinh\s*:(?<b2>.*?)2\.1\.[^:]*:(?<id2>.*?)2\.2\.[^:]*:(?<a2>.*))?$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static List<QuyenSoDocxPage> ReadFile(string filePath)
    {
        // FileShare.ReadWrite: người dùng hay để nguyên file đang mở trong Word khi convert —
        // File.OpenRead (FileShare.Read) sẽ bị Word chặn "being used by another process".
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(stream);
    }

    public static List<QuyenSoDocxPage> Read(Stream docxStream)
    {
        using var zip = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("File không phải .docx hợp lệ (thiếu word/document.xml).");

        XDocument doc;
        using (var entryStream = entry.Open())
        {
            doc = XDocument.Load(entryStream);
        }

        var body = doc.Root?.Element(W + "body")
            ?? throw new InvalidDataException("File .docx không có phần nội dung (w:body).");

        var pages = new List<QuyenSoDocxPage>();
        string? lastTrangSo = null;
        int tableIndex = 0;

        // Duyệt các phần tử cấp cao nhất theo đúng thứ tự tài liệu: đoạn văn giữ lại "Trang số" gần
        // nhất, bảng thì parse thành một trang sổ (nếu đúng cấu trúc).
        foreach (var element in body.Elements())
        {
            if (element.Name == W + "p")
            {
                var text = ParagraphText(element);
                var match = TrangSoPattern.Match(text);
                if (match.Success) lastTrangSo = match.Groups[1].Value.Trim();
                continue;
            }

            if (element.Name != W + "tbl") continue;

            tableIndex++;
            var page = ParseTable(element, tableIndex, lastTrangSo);
            if (page is not null) pages.Add(page);
            // "Trang số" chỉ áp cho đúng bảng ngay sau nó — không để trang sau mượn nhầm nhãn trang trước.
            lastTrangSo = null;
        }

        return pages;
    }

    /// <summary>Parse một bảng thành trang sổ; trả null nếu bảng không có đủ marker mục I + mục II.</summary>
    private static QuyenSoDocxPage? ParseTable(XElement table, int tableIndex, string? trangSo)
    {
        var rows = table.Elements(W + "tr")
            .Select(tr => tr.Elements(W + "tc").Select(CellText).ToList())
            .ToList();

        var keys = rows.Select(cells => Key(string.Join(" ", cells))).ToList();
        bool hasSectionI = keys.Any(k => k.Contains("NGUOISUDUNG", StringComparison.Ordinal));
        bool hasSectionII = keys.Any(k => k.StartsWith("II", StringComparison.Ordinal) && k.Contains("THUADAT", StringComparison.Ordinal));
        if (!hasSectionI || !hasSectionII) return null;

        var page = new QuyenSoDocxPage { PageLabel = trangSo, TableIndex = tableIndex };
        var section = Section.None;

        for (int i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            var key = keys[i];

            // Chuyển mục theo dòng tiêu đề. Lưu ý mục III cũng chứa "SUDUNGDAT" nên phải bắt
            // "NHUNGTHAYDOI" TRƯỚC khi xét mục II.
            if (key.Contains("NHUNGTHAYDOI", StringComparison.Ordinal)) { section = Section.BienDong; continue; }
            if (key.StartsWith("II", StringComparison.Ordinal) && key.Contains("THUADAT", StringComparison.Ordinal) && !key.Contains("HOVATEN", StringComparison.Ordinal))
            {
                section = Section.ThuaDat;
                continue;
            }
            if (key.Contains("NGUOISUDUNG", StringComparison.Ordinal)) { section = Section.NguoiSuDung; continue; }

            switch (section)
            {
                case Section.NguoiSuDung when key.Contains("HOVATEN", StringComparison.Ordinal):
                    ParseNguoiSuDung(string.Join(" ", cells), page);
                    break;

                case Section.ThuaDat:
                    if (IsThuaDatHeaderRow(key, cells) || cells.All(string.IsNullOrWhiteSpace)) break;
                    page.ThuaDat.Add(ToThuaDatRow(cells, page));
                    break;

                case Section.BienDong:
                    if (IsBienDongHeaderRow(key) || cells.All(string.IsNullOrWhiteSpace)) break;
                    page.BienDong.Add(new QuyenSoBienDongRow
                    {
                        SoThua = CellAt(cells, 0),
                        NgayThang = CellAt(cells, 1),
                        NoiDung = string.Join(" ", cells.Skip(2).Where(c => !string.IsNullOrWhiteSpace(c))).Trim()
                    });
                    break;
            }
        }

        return page;
    }

    private static void ParseNguoiSuDung(string rowText, QuyenSoDocxPage page)
    {
        var text = CollapseWhitespace(rowText);
        var match = NguoiSuDungPattern.Match(text);
        if (!match.Success)
        {
            page.CanhBaoDoc.Add("Mục I: không tách được thông tin người sử dụng đất — cần kiểm tra thủ công. "
                                + $"Nội dung đọc được: {Truncate(text, 200)}");
            return;
        }

        AddNguoi(page, match, "n1", "b1", "id1", "a1");
        AddNguoi(page, match, "n2", "b2", "id2", "a2");
    }

    private static void AddNguoi(QuyenSoDocxPage page, Match match, string name, string birth, string id, string addr)
    {
        var nguoi = new QuyenSoNguoiSuDung
        {
            HoTen = CollapseWhitespace(match.Groups[name].Value),
            NamSinh = CollapseWhitespace(match.Groups[birth].Value),
            SoGiayTo = CollapseWhitespace(match.Groups[id].Value),
            DiaChi = CollapseWhitespace(match.Groups[addr].Value)
        };
        if (!nguoi.IsEmpty) page.NguoiSuDung.Add(nguoi);
    }

    private static QuyenSoThuaDatRow ToThuaDatRow(List<string> cells, QuyenSoDocxPage page)
    {
        // Mẫu sổ chuẩn có 11 ô; thiếu ô (bảng bị gộp ô bất thường) thì vẫn đọc phần có và cảnh báo.
        if (cells.Count < 11)
            page.CanhBaoDoc.Add($"Mục II: một dòng thửa chỉ có {cells.Count}/11 ô — dữ liệu có thể lệch cột.");

        return new QuyenSoThuaDatRow
        {
            NgayVaoSo = CellAt(cells, 0),
            SoThua = CellAt(cells, 1),
            SoTo = CellAt(cells, 2),
            DienTichRieng = CellAt(cells, 3),
            DienTichChung = CellAt(cells, 4),
            MucDichSuDung = CellAt(cells, 5),
            ThoiHanSuDung = CellAt(cells, 6),
            NguonGocSuDung = CellAt(cells, 7),
            SoPhatHanh = CellAt(cells, 8),
            SoVaoSo = CellAt(cells, 9),
            GhiChu = CellAt(cells, 10)
        };
    }

    /// <summary>
    /// Dòng header của mục II: dòng tên cột ("Ngày tháng năm vào sổ…"), dòng "Riêng | Chung" và dòng
    /// đánh số cột (mọi ô không rỗng đều là số 1..12).
    /// </summary>
    private static bool IsThuaDatHeaderRow(string key, List<string> cells)
    {
        if (key.Contains("NGAYTHANGNAMVAOSO", StringComparison.Ordinal)) return true;
        if (key == "RIENGCHUNG") return true;

        var nonEmpty = cells.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        if (nonEmpty.Count >= 5 && nonEmpty.All(c => int.TryParse(c, out var n) && n is >= 1 and <= 12))
            return true;

        return false;
    }

    private static bool IsBienDongHeaderRow(string key) =>
        key.Contains("SOTHUTUTHUADAT", StringComparison.Ordinal)
        || (key.Contains("NGAYTHANGNAM", StringComparison.Ordinal) && key.Contains("NOIDUNG", StringComparison.Ordinal));

    private static string CellAt(List<string> cells, int index) =>
        index < cells.Count ? CollapseWhitespace(cells[index]) : "";

    /// <summary>Văn bản một ô: gộp các run <c>w:t</c>, mỗi đoạn văn một dòng, tab thành khoảng trắng.</summary>
    private static string CellText(XElement cell) =>
        string.Join("\n", cell.Elements(W + "p").Select(ParagraphText));

    private static string ParagraphText(XElement paragraph) =>
        string.Concat(paragraph.Descendants().Select(node =>
            node.Name == W + "t" ? node.Value
            : node.Name == W + "tab" ? " "
            : node.Name == W + "br" || node.Name == W + "cr" ? "\n"
            : ""));

    /// <summary>Khoá so khớp header: bỏ dấu + BỎ TOÀN BỘ ký tự không phải chữ/số (chống tách run giữa từ).</summary>
    private static string Key(string text)
    {
        var normalized = GcnFolderRules.Normalize(text);
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string CollapseWhitespace(string value) =>
        Regex.Replace(value ?? "", @"\s+", " ").Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private enum Section { None, NguoiSuDung, ThuaDat, BienDong }
}
