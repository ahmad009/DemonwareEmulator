using System.Security.Cryptography;
using System.Text;

namespace Demonware.Modern;

/// <summary>Per-connection AES/HMAC material derived after lobby auth (S1 scheme).</summary>
public sealed class ModernKeys
{
    private readonly byte[] _sessionKey = new byte[24];
    private readonly byte[] _response = new byte[8];
    private readonly byte[] _hmacKey = new byte[20];
    private readonly byte[] _encKey = new byte[16];
    private readonly byte[] _decKey = new byte[16];
    private readonly StringBuilder _packetHash = new();

    public byte[] ResponseId => (byte[])_response.Clone();
    public byte[] HmacKey => _hmacKey;
    public byte[] EncryptKey => _encKey;
    public byte[] DecryptKey => _decKey;

    public void SetSessionKey(byte[] key)
    {
        if (key.Length < 24) throw new ArgumentException("session key must be 24 bytes");
        Array.Copy(key, _sessionKey, 24);
    }

    public void Queue(byte[] packet)
    {
        if (packet.Length == 0) return;
        foreach (var b in packet) _packetHash.Append((char)b);
    }

    public void DeriveS1()
    {
        var packetBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(_packetHash.ToString());
        var out1 = SHA1.HashData(packetBytes);
        var data3 = HmacSha1(_sessionKey, out1);

        var out2 = new byte[16];
        ExpandHmac(data3, "CLIENTCHAL"u8.ToArray(), out2);
        var out3 = new byte[72];
        ExpandHmac(data3, "BDDATA"u8.ToArray(), out3);

        Array.Copy(out2, 8, _response, 0, 8);
        Array.Copy(out3, 20, _hmacKey, 0, 20);
        Array.Copy(out3, 40, _decKey, 0, 16);
        Array.Copy(out3, 56, _encKey, 0, 16);
    }

    private static void ExpandHmac(byte[] data, byte[] label, byte[] dst)
    {
        var offset = 0;
        byte count = 1;
        byte[]? result = null;
        var buffer = new byte[64];

        var pos = 0;
        Array.Copy(label, 0, buffer, pos, label.Length); pos += label.Length;
        buffer[pos++] = count;
        result = HmacSha1(buffer.AsSpan(0, pos).ToArray(), data);
        Array.Copy(result, 0, dst, 0, Math.Min(20, dst.Length));
        offset = 20;

        while (offset < dst.Length)
        {
            pos = 0;
            Array.Copy(result, 0, buffer, pos, 20); pos += 20;
            Array.Copy(label, 0, buffer, pos, label.Length); pos += label.Length;
            count++;
            buffer[pos++] = count;
            result = HmacSha1(buffer.AsSpan(0, pos).ToArray(), data);
            var copy = Math.Min(20, dst.Length - offset);
            Array.Copy(result, 0, dst, offset, copy);
            offset += copy;
        }
    }

    public static byte[] HmacSha1(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA1(key);
        return hmac.ComputeHash(data);
    }

    public static byte[] AesCbc(byte[] data, byte[] iv16, byte[] key16, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Key = key16;
        aes.IV = iv16;
        using var t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return t.TransformFinalBlock(data, 0, data.Length);
    }
}
