namespace OCR.Business.Auth;

/// <summary>Cau hinh API xac thuc.</summary>
public sealed class AuthApiOptions
{
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}
