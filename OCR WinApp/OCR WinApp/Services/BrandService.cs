using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.UI;

namespace OCR_WinApp.Services;

public sealed class BrandService : IBrandService
{
    private AppearanceConfig _config = BrandDefaults.Create();
    private BrandFlavor _flavor = new();

    public string FlavorKey { get; private set; } = "Green";
    public string DisplayName => _flavor.DisplayName;
    public string Slogan => _flavor.Slogan;
    public Color TintColor { get; private set; } = Color.FromArgb(255, 0x18, 0xA9, 0x57);
    public double TintOpacity { get; private set; } = 0.45;
    public double LuminosityOpacity { get; private set; } = 0.9;
    public Color? TextColor { get; private set; }
    public Uri BannerUri => new($"ms-appx:///Assets/{_flavor.AssetsFolder}/banner.png");
    public string IconIcoPath => Path.Combine(AppContext.BaseDirectory, "Assets", _flavor.AssetsFolder, "icon.ico");

    public void Initialize()
    {
        _config = LoadConfig();

        var key = _config.Flavor;
        if (string.IsNullOrEmpty(key) || !_config.Flavors.ContainsKey(key))
            key = _config.Flavors.Keys.FirstOrDefault() ?? "Green";

        FlavorKey = key;
        _flavor = _config.Flavors.TryGetValue(key, out var f) ? f : new BrandFlavor();

        TintColor = ParseColor(_flavor.TintColor);
        TintOpacity = Clamp01(_flavor.TintOpacity);
        LuminosityOpacity = Clamp01(_flavor.LuminosityOpacity);
        TextColor = string.IsNullOrWhiteSpace(_flavor.TextColor) ? null : ParseColor(_flavor.TextColor);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static AppearanceConfig LoadConfig()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<AppSettingsRoot>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (root?.Appearance is { Flavors.Count: > 0 } config)
                    return config;
            }
        }
        catch
        {
            // Lỗi đọc/parse → dùng cấu hình mặc định.
        }
        return BrandDefaults.Create();
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        var offset = 0;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            offset = 2;
        }
        var r = Convert.ToByte(hex.Substring(offset, 2), 16);
        var g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        var b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }
}
