using System;
using System.IO;
using System.Text.Json;
using OCR.Business.Security;

namespace OCR_WinApp.Services;

public sealed class LoginPreferencesService : ILoginPreferencesService
{
    // File nhị phân mã hoá DPAPI (gắn user + máy). Thay cho login.json plaintext.
    private static readonly string PrefPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OCR WinApp", "login.bin");

    public bool RememberMe { get; private set; }
    public string SavedUsername { get; private set; } = string.Empty;

    public LoginPreferencesService() => Load();

    private void Load()
    {
        try
        {
            if (!File.Exists(PrefPath)) return;
            var json = DataProtector.Unprotect(File.ReadAllBytes(PrefPath));
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            RememberMe = root.TryGetProperty("RememberMe", out var r) && r.GetBoolean();
            SavedUsername = RememberMe && root.TryGetProperty("Username", out var u)
                ? u.GetString() ?? string.Empty
                : string.Empty;
        }
        catch { /* bỏ qua lỗi đọc/giải mã */ }
    }

    public void Save(bool remember, string username)
    {
        RememberMe = remember;
        SavedUsername = remember ? username : string.Empty;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)!);
            var json = JsonSerializer.Serialize(new { RememberMe = remember, Username = SavedUsername });
            File.WriteAllBytes(PrefPath, DataProtector.Protect(json));
        }
        catch { /* bỏ qua lỗi ghi */ }
    }
}
