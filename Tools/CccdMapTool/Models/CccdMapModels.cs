namespace CccdMapTool.Models;

/// <summary>Một dòng của sheet <c>ThongTinCCCD</c> (16 cột A→P, dữ liệu từ dòng 5).</summary>
public sealed record CccdRecord(
    int Row,
    string Serial,
    string LoaiGiayTo,
    string SoGiayTo,
    string HoTen,
    string NgaySinh,
    string GioiTinh,
    string QuocTich,
    string NoiThuongTru,
    string NgayCap,
    string NoiCap);

/// <summary>
/// Toạ độ cột của MỘT khối chủ trong sheet <c>KeKhaiDangKy</c>. Khối chủ (CHU_/GT_) và khối vợ chồng
/// (VC_/GT_VC_) có cùng tập trường nên dùng chung kiểu này, chỉ khác chữ cái cột.
/// </summary>
public sealed record OwnerBlock(
    string Name,
    string ColHoTen,
    string ColSoGiayTo,
    string ColLoaiGiayTo,
    string ColNgaySinh,
    string ColGioiTinh,
    string ColQuocTich,
    string ColDiaChi,
    string ColNgayCap,
    string ColNoiCap);

/// <summary>Kết quả tra một khối chủ trong nhóm CCCD cùng serial.</summary>
public sealed record MatchOutcome(CccdRecord? Record, string Reason, bool Ambiguous)
{
    public static MatchOutcome Found(CccdRecord record, string by) => new(record, by, false);
    public static MatchOutcome NotFound() => new(null, "", false);
    public static MatchOutcome AmbiguousBy(string reason) => new(null, reason, true);
}

/// <summary>Số liệu tổng kết một lần chạy, dùng cho log GUI lẫn CLI.</summary>
public sealed record MapResult(
    string OutputPath,
    int RowsScanned,
    int BlocksMatched,
    int FieldsFilled,
    int RowsWarned,
    int CccdTotal,
    int CccdUnused);
