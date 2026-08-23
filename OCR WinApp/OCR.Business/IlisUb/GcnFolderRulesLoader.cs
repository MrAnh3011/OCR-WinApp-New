using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OCR.Business.IlisUb;

/// <summary>
/// Đọc gcn-ilis-ub-rules.json. Cố ý KHÔNG nuốt lỗi như AppSettingsLoader: quy tắc sai mà vẫn Export
/// thì người dùng nhận về một cây thư mục sai âm thầm — thà chặn Export và báo rõ.
/// </summary>
public sealed class GcnFolderRulesLoader : IGcnFolderRulesLoader
{
    private sealed class RulesDto
    {
        public string? GcnKeyword { get; set; }
        public List<GroupDto>? Groups { get; set; }
    }

    private sealed class GroupDto
    {
        public string? Suffix { get; set; }
        public List<string>? Keywords { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public GcnFolderRules Load(string rulesPath)
    {
        var resolved = Resolve(rulesPath);
        if (!File.Exists(resolved))
            throw new InvalidOperationException($"Không tìm thấy file quy tắc gộp/đổi tên: {resolved}");

        RulesDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RulesDto>(File.ReadAllText(resolved), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"File quy tắc sai cú pháp JSON: {resolved}. {ex.Message}", ex);
        }

        if (dto is null)
            throw new InvalidOperationException($"File quy tắc rỗng: {resolved}");

        var keyword = (dto.GcnKeyword ?? "").Trim();
        if (keyword.Length == 0)
            throw new InvalidOperationException($"File quy tắc thiếu \"GcnKeyword\": {resolved}");

        var groups = new List<GcnFolderRuleGroup>();
        var seenSuffix = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in dto.Groups ?? new List<GroupDto>())
        {
            var suffix = (g.Suffix ?? "").Trim();
            if (suffix.Length == 0)
                throw new InvalidOperationException($"File quy tắc có nhóm thiếu \"Suffix\": {resolved}");
            if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException($"\"Suffix\" chứa ký tự không hợp lệ cho tên file: {suffix}");
            if (!seenSuffix.Add(suffix))
                throw new InvalidOperationException($"File quy tắc có hai nhóm trùng \"Suffix\": {suffix}");
            if (GcnFolderRules.Normalize(suffix) == GcnFolderRules.Normalize(keyword))
                throw new InvalidOperationException(
                    $"\"Suffix\" = \"{suffix}\" trùng \"GcnKeyword\" = \"{keyword}\" — file gộp sẽ đè lên chính file GCN thật, phải đổi Suffix khác.");

            groups.Add(new GcnFolderRuleGroup
            {
                Suffix = suffix,
                Keywords = (g.Keywords ?? new List<string>())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.Trim())
                    .ToList()
            });
        }

        return new GcnFolderRules { GcnKeyword = keyword, Groups = groups };
    }

    private static string Resolve(string rulesPath)
    {
        if (string.IsNullOrWhiteSpace(rulesPath)) return rulesPath ?? "";
        if (Path.IsPathRooted(rulesPath)) return rulesPath;
        if (File.Exists(rulesPath)) return Path.GetFullPath(rulesPath);
        return Path.Combine(AppContext.BaseDirectory, rulesPath);
    }
}
