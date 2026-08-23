using System;
using System.Security.Cryptography;
using System.Text;

namespace OCR.Business.Security;

/// <summary>
/// Mã hoá / giải mã dữ liệu cục bộ bằng DPAPI (CurrentUser) + entropy là HardwareId.
/// Dữ liệu chỉ giải mã được trên chính user + máy đó (port từ CredentialHelper GcnOcrApp).
/// </summary>
public static class DataProtector
{
    private static byte[] Entropy => Encoding.UTF8.GetBytes(HardwareIdHelper.GetHardwareId());

    public static byte[] Protect(string plain)
        => ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);

    public static string? Unprotect(byte[] cipher)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }
}
