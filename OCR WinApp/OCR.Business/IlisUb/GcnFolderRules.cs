using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OCR.Business.IlisUb;

/// <summary>Một nhóm hậu tố trong file quy tắc: mọi file khớp nhóm sẽ được gộp thành {nhãn}-{Suffix}.pdf.</summary>
public sealed class GcnFolderRuleGroup
{
    public string Suffix { get; init; } = "";

    /// <summary>Thứ tự phần tử ở đây CHÍNH LÀ thứ tự trang khi gộp.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}

/// <summary>Kết quả so khớp một tên file với bộ quy tắc.</summary>
public sealed record GcnFolderRuleMatch(int GroupIndex, string Suffix, int KeywordIndex);

/// <summary>
/// Bộ quy tắc đọc từ gcn-ilis-ub-rules.json. Mọi phép so khớp đi qua <see cref="Normalize"/> nên
/// KHÔNG phân biệt hoa/thường và KHÔNG phân biệt dấu tiếng Việt.
/// </summary>
public sealed class GcnFolderRules
{
    public string GcnKeyword { get; init; } = "GCN";

    public IReadOnlyList<GcnFolderRuleGroup> Groups { get; init; } = Array.Empty<GcnFolderRuleGroup>();

    /// <summary>
    /// Chuẩn hoá dùng chung cho MỌI phép so khớp tên file: trim → bỏ dấu tiếng Việt (NFD, loại
    /// NonSpacingMark) → hoa hoá. Dùng đúng một hàm này để tên file và từ khoá luôn cùng hệ quy chiếu.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            // Chữ Đ/đ tách bằng NFD không ra dấu phụ nên phải xử lý tay.
            sb.Append(c switch { 'Đ' => 'D', 'đ' => 'd', _ => c });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>Tên file (KHÔNG kể phần mở rộng) có chứa cụm <see cref="GcnKeyword"/> không.</summary>
    public bool IsGcnFileName(string fileNameWithoutExtension)
    {
        var keyword = Normalize(GcnKeyword);
        if (keyword.Length == 0) return false;
        return Normalize(fileNameWithoutExtension).Contains(keyword, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nhóm khớp ĐẦU TIÊN trong <see cref="Groups"/> kèm chỉ số từ khoá khớp (dùng để sắp thứ tự
    /// trang khi gộp). Trả null nếu không nhóm nào khớp.
    /// </summary>
    public GcnFolderRuleMatch? Match(string fileNameWithoutExtension)
    {
        var name = Normalize(fileNameWithoutExtension);
        if (name.Length == 0) return null;

        for (int g = 0; g < Groups.Count; g++)
        {
            var keywords = Groups[g].Keywords;
            for (int k = 0; k < keywords.Count; k++)
            {
                var keyword = Normalize(keywords[k]);
                if (keyword.Length == 0) continue;
                if (name.Contains(keyword, StringComparison.Ordinal))
                    return new GcnFolderRuleMatch(g, Groups[g].Suffix, k);
            }
        }

        return null;
    }
}
