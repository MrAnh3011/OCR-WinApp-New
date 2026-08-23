using System;
using System.Runtime.CompilerServices;

namespace OCR.Business.Security;

/// <summary>Giải mã chuỗi bí mật XOR split-key tại compile-time.</summary>
internal static class SecretHelper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Ka() => new byte[] { 0x42, 0x1A, 0x7C, 0xE3 };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Kb() => new byte[] { 0x8F, 0x55, 0xC9, 0xD0 };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Kc() => new byte[] { 0x3B, 0x96, 0x64, 0xAF };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Kd() => new byte[] { 0x2D, 0xF8, 0x17, 0x8A };

    private static byte[]? _key;
    private static readonly object _lock = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] GetKey()
    {
        if (_key is not null) return _key;
        lock (_lock)
        {
            if (_key is not null) return _key;
            var a = Ka();
            var b = Kb();
            var c = Kc();
            var d = Kd();
            _key = new byte[16];
            Buffer.BlockCopy(a, 0, _key, 0, 4);
            Buffer.BlockCopy(b, 0, _key, 4, 4);
            Buffer.BlockCopy(c, 0, _key, 8, 4);
            Buffer.BlockCopy(d, 0, _key, 12, 4);
            return _key;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string Decode(byte[] encoded)
    {
        var key = GetKey();
        var result = new byte[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
            result[i] = (byte)(encoded[i] ^ key[i % key.Length]);
        return System.Text.Encoding.UTF8.GetString(result);
    }
}
