using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OCR.Business.Ai;

/// <summary>
/// Chuẩn hoá chuỗi JSON do mô hình AI trả về (LỚP PHÒNG THỦ CHẮC CHẮN):
/// bóc rào ```json ... ```; cắt phần rác / dấu ngoặc đóng dư sau khi object (hoặc mảng) gốc
/// đã đóng cân bằng. Model đôi khi trả dư 1–2 dấu '}' ở đuôi JSON lồng sâu; hàm này bảo đảm
/// phần trả về là JSON ĐẦU TIÊN có ngoặc cân bằng, tính từ '{' hoặc '[' đầu tiên,
/// và an toàn với ngoặc nằm trong chuỗi "..." (có xử lý escape '\').
/// </summary>
public static class AiJsonText
{
    /// <summary>Bóc rào ```json ... ``` nếu có; trả phần bên trong đã trim.</summary>
    public static string StripFences(string? text)
    {
        var raw = (text ?? "").Trim();
        var m = Regex.Match(raw, @"^```(?:json)?\s*([\s\S]*?)\s*```\s*$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : raw;
    }

    /// <summary>
    /// Bóc fence rồi trả JSON đầu tiên có ngoặc cân bằng tính từ '{' hoặc '[' đầu tiên,
    /// cắt sạch mọi ký tự dư phía sau (kể cả dấu '}' hay ']' thừa, BOM, giải thích lẫn vào).
    /// Nếu chuỗi bị cắt cụt (thiếu ngoặc đóng) thì trả phần từ dấu mở đến hết để lớp retry xử lý.
    /// </summary>
    public static string ExtractBalancedJson(string? text)
    {
        var s = StripFences(text);
        if (s.Length == 0) return s;

        int start = s.IndexOfAny(new[] { '{', '[' });
        if (start < 0) return s;

        int depth = 0;
        bool inString = false, escaped = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    if (depth == 0) return s.Substring(start, i - start + 1);
                    break;
            }
        }

        return s.Substring(start);
    }

    /// <summary>
    /// Model đôi khi tự chèn nhầm 1 dấu '"' giữa 2 nửa của cùng một dãy số dài (VD mã vạch bị tách
    /// thành <c>71982"0000232"</c> thay vì một chuỗi <c>"719820000232"</c> liền mạch) — JSON hỏng cú
    /// pháp ngay từ bước parse dù ngoặc vẫn cân bằng. Quét chuỗi (bỏ qua nội dung nằm trong "...") và
    /// nối lại 2 nửa số liền kề bị dấu ngoặc kép thừa chia cắt, giữ nguyên giá trị số (không tự thêm
    /// ngoặc kép ở đây — để <see cref="QuoteInvalidLeadingZeroNumbers"/> xử lý tiếp phần leading zero
    /// sau khi đã nối lại). Không ảnh hưởng JSON hợp lệ sẵn có nên gọi vô điều kiện.
    /// </summary>
    public static string MergeSplitDigitStrings(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var sb = new StringBuilder(text.Length + 16);
        bool inString = false, escaped = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                int j = i;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                var digits = new StringBuilder();
                digits.Append(text, i, j - i);

                // Nối tiếp mọi chuỗi "<digits>" nằm NGAY SAU (không có dấu phân cách) — đó là các
                // nửa còn lại của cùng một dãy số bị model chèn nhầm ngoặc kép ở giữa. Chỉ lấy phần
                // chữ số, BỎ dấu ngoặc kép thừa (không được giữ lại trong kết quả).
                while (j < text.Length && text[j] == '"')
                {
                    int digitsStart = j + 1;
                    int k = digitsStart;
                    while (k < text.Length && char.IsDigit(text[k])) k++;
                    if (k == digitsStart || k >= text.Length || text[k] != '"') break;
                    digits.Append(text, digitsStart, k - digitsStart);
                    j = k + 1;
                }

                sb.Append(digits);
                i = j;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Model đôi khi trả trường dạng mã (VD mã vạch "0719220000680") thành số JSON thô thay vì chuỗi
    /// có ngoặc kép. Số JSON không cho phép số 0 đứng đầu (trừ số 0 hoặc 0.x) nên khi giá trị thật có
    /// số 0 đứng đầu, JSON hỏng cú pháp ngay từ bước parse. Quét chuỗi (bỏ qua nội dung nằm trong
    /// "...") và bọc lại các số dạng này vào ngoặc kép để JSON hợp lệ trở lại, giữ nguyên giá trị.
    /// Không ảnh hưởng JSON hợp lệ sẵn có (số hợp lệ không bao giờ có leading zero) nên gọi vô điều kiện.
    /// </summary>
    public static string QuoteInvalidLeadingZeroNumbers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var sb = new StringBuilder(text.Length + 16);
        bool inString = false, escaped = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                // Đọc NGUYÊN CẢ dãy số trước khi quyết định — nếu chỉ nhìn 1 ký tự '0' + ký tự kế
                // tiếp thì một số "00" nằm GIỮA một dãy số dài hợp lệ (VD 719820000232) cũng bị hiểu
                // nhầm là số mới bắt đầu, tự chèn dấu '"' vào giữa — tức là tự tạo ra đúng lỗi split
                // digit mà lớp này phải sửa.
                int j = i;
                while (j < text.Length && char.IsDigit(text[j])) j++;

                if (c == '0' && j - i > 1)
                {
                    // Dấu '-' (nếu có) đã được append ở vòng trước — đưa vào trong ngoặc kép luôn,
                    // tránh sinh ra -"0123" (số có dấu trừ đứng trước chuỗi, vẫn sai cú pháp).
                    bool hasSign = sb.Length > 0 && sb[^1] == '-';
                    if (hasSign) sb.Length--;

                    sb.Append('"');
                    if (hasSign) sb.Append('-');
                    sb.Append(text, i, j - i);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(text, i, j - i);
                }

                i = j;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>True nếu chuỗi parse được thành JSON hợp lệ.</summary>
    public static bool IsParsableJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
