namespace OCR_WinApp.Services;

/// <summary>Lưu/đọc tùy chọn "Ghi nhớ tôi" và tên đăng nhập đã lưu (file cục bộ).</summary>
public interface ILoginPreferencesService
{
    bool RememberMe { get; }
    string SavedUsername { get; }

    /// <summary>Lưu trạng thái ghi nhớ. Nếu <paramref name="remember"/> = false thì xóa tên đã lưu.</summary>
    void Save(bool remember, string username);
}
