using System.Reflection;
using System.Security.Cryptography;
using Demonware.Core.Crypto;

namespace Demonware.Core.Crypto;

public static class DwCrypto
{
    public static byte[] TigerIv(uint seed)
    {
        using var tiger = new TigerHash();
        return tiger.ComputeHash(BitConverter.GetBytes(seed));
    }

    /// <summary>Tiger hash — typically 24 bytes (used as 3DES key material).</summary>
    public static byte[] Tiger(byte[] data)
    {
        using var tiger = new TigerHash();
        var hash = tiger.ComputeHash(data);
        if (hash.Length == 24) return hash;
        var key = new byte[24];
        Array.Copy(hash, 0, key, 0, Math.Min(hash.Length, 24));
        return key;
    }

    public static byte[] RandomBytes(int length)
    {
        var buf = new byte[length];
        RandomNumberGenerator.Fill(buf);
        // Demonware historically preferred non-zero; keep non-zero where possible
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] == 0) buf[i] = 0x13;
        return buf;
    }

    public static byte[] TripleDesDecrypt(byte[] iv24, byte[] key24, byte[] data)
    {
        if (key24.Length != 24) throw new ArgumentException("key");
        if (iv24.Length != 24) throw new ArgumentException("iv");
        using var des = TripleDES.Create();
        des.Padding = PaddingMode.None;
        des.Mode = CipherMode.CBC;
        var output = new byte[data.Length];
        try
        {
            using var transform = CreateTransform(des, key24, iv24, encrypt: false);
            using var input = new MemoryStream(data);
            using var crypto = new CryptoStream(input, transform, CryptoStreamMode.Read);
            _ = crypto.Read(output, 0, output.Length);
        }
        catch { /* truncated / bad padding — return zeros like upstream */ }
        return output;
    }

    public static byte[] TripleDesEncrypt(byte[] iv24, byte[] key24, byte[] data)
    {
        if (key24.Length != 24) throw new ArgumentException("key");
        if (iv24.Length != 24) throw new ArgumentException("iv");
        using var des = TripleDES.Create();
        des.Padding = PaddingMode.Zeros;
        des.Mode = CipherMode.CBC;
        using var output = new MemoryStream();
        using (var crypto = new CryptoStream(output, CreateTransform(des, key24, iv24, encrypt: true), CryptoStreamMode.Write))
        {
            crypto.Write(data, 0, data.Length);
            crypto.FlushFinalBlock();
        }
        return output.ToArray();
    }

    /// <summary>
    /// Game keys (repeating 0x1337, all-zero, …) are often rejected as "weak" by .NET —
    /// fall back to the private _NewEncryptor path used by older runtimes.
    /// </summary>
    private static ICryptoTransform CreateTransform(SymmetricAlgorithm des, byte[] key, byte[] iv, bool encrypt)
    {
        try
        {
            return encrypt ? des.CreateEncryptor(key, iv) : des.CreateDecryptor(key, iv);
        }
        catch (CryptographicException)
        {
            var mi = des.GetType().GetMethod("_NewEncryptor", BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new CryptographicException("TripleDES weak-key bypass unavailable");
            return (ICryptoTransform)mi.Invoke(des, [key, des.Mode, iv, des.FeedbackSize, encrypt ? 0 : 1])!;
        }
    }
}

