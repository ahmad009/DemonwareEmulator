using System.Text;
using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;

namespace Demonware.Modern;

public static class Auth3Keys
{
    /// <summary>Shared default session key (24 × 0x1337) — Auth3 HTTP and AES lobby agree on this.</summary>
    public static readonly byte[] DefaultSessionKey = Encoding.GetEncoding("ISO-8859-1").GetBytes(
        "\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37\x13\x37");

    private static byte[] _shared = DefaultSessionKey;

    public static void SetShared(byte[] key)
    {
        if (key.Length < 24) return;
        var copy = new byte[24];
        Array.Copy(key, copy, 24);
        _shared = copy;
    }

    public static byte[] Shared => _shared;
}

/// <summary>AES lobby framing (magic 0xAB) — one instance per TCP connection.</summary>
public sealed class ModernSession
{
    private readonly ModernKeys _keys = new();
    private readonly MemoryStream _pending = new();
    private readonly FileStore _store;
    private int _msgCount;
    private bool _authed;

    public ModernSession(FileStore store) => _store = store;

    public void Handle(TcpConnection conn, byte[] chunk)
    {
        _pending.Write(chunk, 0, chunk.Length);
        _pending.Position = 0;

        try
        {
            while (_pending.Length - _pending.Position >= 4)
            {
                var start = _pending.Position;
                using var reader = new BinaryReader(_pending, Encoding.ASCII, leaveOpen: true);
                int size;
                try { size = reader.ReadInt32(); }
                catch { break; }

                if (size <= 0)
                {
                    conn.Send([0, 0, 0, 0]);
                    _pending.SetLength(0);
                    return;
                }

                if (size == 0xC8)
                {
                    var c8 = reader.ReadInt32();
                    var rem = ReadRemaining(_pending);
                    _keys.Queue(BitConverter.GetBytes(size));
                    _keys.Queue(BitConverter.GetBytes(c8));
                    _keys.Queue(rem);

                    var packet2 = new byte[]
                    {
                        0x16, 0x00, 0x00, 0x00, 0xab, 0x81, 0xd2, 0x00, 0x00, 0x00,
                        0x13, 0x37, 0x13, 0x37, 0x13, 0x37, 0x13, 0x37, 0x13, 0x37,
                        0x13, 0x37, 0x13, 0x37, 0x13, 0x37
                    };
                    _keys.Queue(packet2);
                    conn.Send(packet2);
                    Log.Debug("Modern", "header ack");
                    Compact(_pending);
                    return;
                }

                if (_pending.Length - start < 4 + size)
                {
                    _pending.Position = start;
                    break;
                }

                var magic = reader.ReadByte();
                if (magic == 0xAB)
                {
                    var type = reader.ReadByte();
                    if (type == 0x82)
                    {
                        _pending.Position = start;
                        var full = new byte[4 + size];
                        _ = _pending.Read(full, 0, full.Length);
                        var toHash = new byte[full.Length - 8];
                        Array.Copy(full, toHash, toHash.Length);
                        _keys.Queue(toHash);
                        _keys.SetSessionKey(Auth3Keys.Shared);
                        _keys.DeriveS1();
                        _authed = true;

                        var resp = new byte[14];
                        resp[0] = 0x0A;
                        resp[4] = 0xAB;
                        resp[5] = 0x83;
                        Array.Copy(_keys.ResponseId, 0, resp, 6, 8);
                        conn.Send(resp);
                        Log.Debug("Modern", "auth done");
                        Compact(_pending);
                        return;
                    }

                    if (type == 0x85)
                    {
                        _ = reader.ReadUInt32();
                        var seed = reader.ReadBytes(16);
                        var remain = size - (1 + 1 + 4 + 16);
                        if (remain < 8 || !_authed)
                        {
                            Log.Error("Modern", "bad encrypted frame");
                            break;
                        }

                        var encAndHash = reader.ReadBytes(remain);
                        var enc = new byte[encAndHash.Length - 8];
                        Array.Copy(encAndHash, enc, enc.Length);
                        var dec = ModernKeys.AesCbc(enc, seed, _keys.DecryptKey, encrypt: false);

                        using var ms = new MemoryStream(dec);
                        using var br = new BinaryReader(ms);
                        _ = br.ReadUInt32();
                        _ = br.ReadByte(); // 0x86
                        var serviceId = br.ReadByte();
                        var serviceData = br.ReadBytes((int)Math.Max(0, ms.Length - ms.Position));
                        ModernServices.Dispatch(conn, this, _store, serviceId, serviceData);
                        Compact(_pending);
                        continue;
                    }
                }

                Log.Error("Modern", $"unknown frame size={size} magic={magic:X2}");
                _pending.Position = start + 4 + size;
                Compact(_pending);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Modern", ex.ToString());
        }
    }

    public void SendServiceReply(TcpConnection conn, byte[] body)
    {
        using var encBuf = new MemoryStream();
        using (var bw = new BinaryWriter(encBuf))
        {
            bw.Write((uint)body.Length);
            bw.Write((byte)1);
            bw.Write(body);
        }

        var aligned = encBuf.ToArray();
        var size = (~15) & (aligned.Length + 15);
        Array.Resize(ref aligned, size);

        var seed = Encoding.GetEncoding("ISO-8859-1").GetBytes(
            "\x5E\xED\x5E\xED\x5E\xED\x5E\xED\x5E\xED\x5E\xED\x5E\xED\x5E\xED");
        var encData = ModernKeys.AesCbc(aligned, seed, _keys.EncryptKey, encrypt: true);

        _msgCount++;
        using var resp = new MemoryStream();
        using (var rw = new BinaryWriter(resp))
        {
            rw.Write(30 + encData.Length);
            rw.Write((byte)0xAB);
            rw.Write((byte)0x85);
            rw.Write(_msgCount);
            rw.Write(seed);
            rw.Write(encData);
        }

        var soFar = resp.ToArray();
        var hash = ModernKeys.HmacSha1(_keys.HmacKey, soFar);
        Array.Resize(ref hash, 8);

        using var final = new MemoryStream();
        using (var fw = new BinaryWriter(final))
        {
            fw.Write(30 + encData.Length);
            fw.Write((byte)0xAB);
            fw.Write((byte)0x85);
            fw.Write(_msgCount);
            fw.Write(seed);
            fw.Write(encData);
            fw.Write(hash);
        }
        conn.Send(final.ToArray());
    }

    public void SendEmptyOk(TcpConnection conn, byte taskId)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(ModernServices.NextTransactionId());
        bw.Write((uint)0);
        bw.Write(taskId);
        bw.Write((uint)0);
        SendServiceReply(conn, ms.ToArray());
    }

    private static byte[] ReadRemaining(MemoryStream ms)
    {
        var left = (int)(ms.Length - ms.Position);
        if (left <= 0) return [];
        var buf = new byte[left];
        _ = ms.Read(buf, 0, left);
        return buf;
    }

    private static void Compact(MemoryStream ms)
    {
        var left = (int)(ms.Length - ms.Position);
        if (left <= 0) { ms.SetLength(0); ms.Position = 0; return; }
        var buf = new byte[left];
        _ = ms.Read(buf, 0, left);
        ms.SetLength(0);
        ms.Write(buf, 0, buf.Length);
        ms.Position = 0;
    }
}
