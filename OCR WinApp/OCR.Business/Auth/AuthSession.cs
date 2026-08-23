namespace OCR.Business.Auth;

/// <summary>Thong tin phien dang nhap va han muc OCR cua nguoi dung.</summary>
public sealed class AuthSession
{
    public string AccessToken { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string FullName { get; init; } = "";
    public OcrQuotaInfo? Quota { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? UserName : FullName;
}

public sealed class OcrQuotaInfo
{
    public string UserId { get; set; } = "";
    public int SoFileChoPhep { get; set; }
    public int SoFileDaDoc { get; set; }
    public int SoFileConLai { get; set; }
    public bool TrangThaiChoPhepDoc { get; set; }

    public int HanMuc => SoFileChoPhep;
    public int ConLai => SoFileConLai > 0 ? SoFileConLai : Math.Max(0, SoFileChoPhep - SoFileDaDoc);
    public bool IsActive => TrangThaiChoPhepDoc;
}
