using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace OCR.Business.Models;

/// <summary>
/// Một dòng trạng thái tách GCN cho 1 file PDF gốc. INotifyPropertyChanged để lưới cập nhật real-time.
/// </summary>
public class SplitGcnRecord : INotifyPropertyChanged
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
    /// <summary>Tên hiển thị trên lưới: đường dẫn tương đối từ thư mục đã chọn tới file; không set thì fallback tên file.</summary>
    public string FileName
    {
        get => string.IsNullOrEmpty(_fileName) ? Path.GetFileName(FilePath) : _fileName!;
        set => Set(ref _fileName, value);
    }

    private string _trangThai = "Chờ xử lý";
    public string TrangThai { get => _trangThai; set => Set(ref _trangThai, value); }

    private int _soLuongGcn;
    /// <summary>Số GCN model nhận diện trong file.</summary>
    public int SoLuongGcn { get => _soLuongGcn; set => Set(ref _soLuongGcn, value); }

    private int _soFileTao;
    /// <summary>Số file PDF con đã cắt ra.</summary>
    public int SoFileTao { get => _soFileTao; set => Set(ref _soFileTao, value); }

    private int _soBoHoanChinh;
    /// <summary>Số bộ kết quả đã tạo đủ file bắt buộc của biến thể hiện tại.</summary>
    public int SoBoHoanChinh { get => _soBoHoanChinh; set => Set(ref _soBoHoanChinh, value); }

    private string _thieuFile = "";
    /// <summary>Tên các thư mục kết quả Tách GCN New không tạo đủ bộ 3 file GCN/GT/GTK.</summary>
    public string ThieuFile { get => _thieuFile; set => Set(ref _thieuFile, value); }

    public string? ErrorMessage { get; set; }

    public void UpdateFrom(SplitGcnRecord src)
    {
        SoLuongGcn = src.SoLuongGcn;
        SoFileTao = src.SoFileTao;
        SoBoHoanChinh = src.SoBoHoanChinh;
        ThieuFile = src.ThieuFile;
        ErrorMessage = src.ErrorMessage;
        TrangThai = src.TrangThai;
    }
}
