using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace OCR.Business.Models;

/// <summary>
/// Một dòng kết quả OCR "Đất Uỷ Ban" (Mẫu số 15 - Đơn đăng ký đất đai).
/// INotifyPropertyChanged để lưới cập nhật real-time.
/// </summary>
public class UyBanRecord : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileName)));
        }
    }
    private string? _fileName;
    /// <summary>Tên hiển thị trên lưới: đường dẫn tương đối từ thư mục đã chọn tới file; không set thì fallback tên file.</summary>
    public string FileName
    {
        get => string.IsNullOrEmpty(_fileName) ? Path.GetFileName(FilePath) : _fileName!;
        set => Set(ref _fileName, value);
    }

    private string _trangThai = "Chờ xử lý";
    public string TrangThai { get => _trangThai; set => Set(ref _trangThai, value); }

    private string _hoVaTen = "";
    public string HoVaTen { get => _hoVaTen; set => Set(ref _hoVaTen, value); }

    private string _diaChi4 = "";
    public string DiaChi4 { get => _diaChi4; set => Set(ref _diaChi4, value); }

    private string _thuaDatSo = "";
    public string ThuaDatSo { get => _thuaDatSo; set => Set(ref _thuaDatSo, value); }

    private string _toBanDoSo = "";
    public string ToBanDoSo { get => _toBanDoSo; set => Set(ref _toBanDoSo, value); }

    private string _diaChi5 = "";
    public string DiaChi5 { get => _diaChi5; set => Set(ref _diaChi5, value); }

    private string _dienTich6 = "";
    public string DienTich6 { get => _dienTich6; set => Set(ref _dienTich6, value); }

    private string _suDungChung = "";
    public string SuDungChung { get => _suDungChung; set => Set(ref _suDungChung, value); }

    private string _suDungRieng = "";
    public string SuDungRieng { get => _suDungRieng; set => Set(ref _suDungRieng, value); }

    private string _mucDich7 = "";
    public string MucDich7 { get => _mucDich7; set => Set(ref _mucDich7, value); }

    private string _thoiHan8 = "";
    public string ThoiHan8 { get => _thoiHan8; set => Set(ref _thoiHan8, value); }

    public string? ErrorMessage { get; set; }

    public void UpdateFrom(UyBanRecord src)
    {
        HoVaTen = src.HoVaTen;
        DiaChi4 = src.DiaChi4;
        ThuaDatSo = src.ThuaDatSo;
        ToBanDoSo = src.ToBanDoSo;
        DiaChi5 = src.DiaChi5;
        DienTich6 = src.DienTich6;
        SuDungChung = src.SuDungChung;
        SuDungRieng = src.SuDungRieng;
        MucDich7 = src.MucDich7;
        ThoiHan8 = src.ThoiHan8;
        ErrorMessage = src.ErrorMessage;
        TrangThai = src.TrangThai;
    }
}
