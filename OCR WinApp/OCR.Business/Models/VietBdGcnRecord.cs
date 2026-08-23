using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OCR.Business.Models;

/// <summary>
/// Một dòng kết quả lưới "OCR GCN VietBD" — mỗi DÒNG = một thửa đất. INotifyPropertyChanged để cập nhật real-time.
/// Tách riêng khỏi <see cref="NewGcnRecord"/> để lưới hai màn tiến hoá độc lập.
/// </summary>
public class VietBdGcnRecord : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string FilePath { get; set; } = "";

    private string _fileName = "";
    public string FileName { get => _fileName; set => Set(ref _fileName, value); }

    private string _trangThai = "Chờ xử lý";
    public string TrangThai { get => _trangThai; set => Set(ref _trangThai, value); }

    private string _soSerial = "";
    public string SoSerial { get => _soSerial; set => Set(ref _soSerial, value); }

    private string _chuSuDung = "";
    public string ChuSuDung { get => _chuSuDung; set => Set(ref _chuSuDung, value); }

    private string _soThua = "";
    public string SoThua { get => _soThua; set => Set(ref _soThua, value); }

    private string _soTo = "";
    public string SoTo { get => _soTo; set => Set(ref _soTo, value); }

    private string _tongDienTich = "";
    public string TongDienTich { get => _tongDienTich; set => Set(ref _tongDienTich, value); }

    private string _loaiDat = "";
    public string LoaiDat { get => _loaiDat; set => Set(ref _loaiDat, value); }

    private string _doTinCay = "";
    public string DoTinCay { get => _doTinCay; set => Set(ref _doTinCay, value); }
}
