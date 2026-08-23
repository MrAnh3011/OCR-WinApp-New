using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace OCR.Business.Models;

/// <summary>
/// Một dòng lưới của màn "Xóa trang trắng" — mỗi dòng là 1 file PDF nguồn.
/// INotifyPropertyChanged để lưới cập nhật real-time trong lúc quét.
/// </summary>
public class BlankPageRecord : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string FilePath { get; set; } = "";

    private string? _fileName;
    /// <summary>Hiển thị đường dẫn tương đối từ thư mục đã chọn; không set thì fallback tên file.</summary>
    public string FileName
    {
        get => string.IsNullOrEmpty(_fileName) ? Path.GetFileName(FilePath) : _fileName!;
        set => Set(ref _fileName, value);
    }

    private string _trangThai = "Chờ xử lý";
    public string TrangThai { get => _trangThai; set => Set(ref _trangThai, value); }

    private string _tongTrang = "";
    /// <summary>Chuỗi rỗng khi chưa quét xong hoặc khi PDF mở không được.</summary>
    public string TongTrang { get => _tongTrang; set => Set(ref _tongTrang, value); }

    private string _soTrangXoa = "";
    public string SoTrangXoa { get => _soTrangXoa; set => Set(ref _soTrangXoa, value); }

    private string _cacTrangDaXoa = "";
    /// <summary>Số thứ tự các trang trắng, đánh số từ 1 theo file gốc.</summary>
    public string CacTrangDaXoa { get => _cacTrangDaXoa; set => Set(ref _cacTrangDaXoa, value); }
}
