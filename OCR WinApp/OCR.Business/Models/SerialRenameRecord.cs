using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace OCR.Business.Models;

/// <summary>
/// Một dòng lưới của màn "Đổi tên theo Serial" — mỗi dòng là 1 file GCN nguồn.
/// INotifyPropertyChanged để lưới cập nhật real-time trong lúc đọc.
/// </summary>
public class SerialRenameRecord : INotifyPropertyChanged
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

    private string _serial = "";
    /// <summary>Số serial OCR đọc được; rỗng nếu không đọc được.</summary>
    public string Serial { get => _serial; set => Set(ref _serial, value); }

    private string _tenMoi = "";
    /// <summary>Tên thư mục / tên file sẽ đặt khi Export, để người dùng rà soát TRƯỚC khi ghi.</summary>
    public string TenMoi { get => _tenMoi; set => Set(ref _tenMoi, value); }

    private string _trangThai = "Chờ xử lý";
    public string TrangThai { get => _trangThai; set => Set(ref _trangThai, value); }
}
