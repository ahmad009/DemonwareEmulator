using Demonware.Core;
using Demonware.Core.Crypto;
using Demonware.Core.Net;
using Demonware.Core.Store;
using Demonware.Legacy.Buffers;

namespace Demonware.Legacy.Protocol;

public sealed class LegacyMessage
{
    public required TcpConnection Connection { get; init; }
    public required SessionKeyMap Keys { get; init; }
    public int Type { get; set; }
    public BitBuffer? Bit { get; set; }
    public ByteBuffer? Bytes { get; set; }
    public byte[] Raw { get; set; } = [];

    public static LegacyMessage FromRequest(TcpConnection conn, SessionKeyMap keys, byte[] body, int type)
        => new()
        {
            Connection = conn,
            Keys = keys,
            Type = type,
            Raw = body,
            Bit = new BitBuffer(body),
            Bytes = new ByteBuffer(body)
        };

    public LegacyMessage MakeReply(byte type, bool isBit)
    {
        var buf = new byte[8192];
        var msg = new LegacyMessage
        {
            Connection = Connection,
            Keys = Keys,
            Type = type,
            Raw = buf
        };
        if (isBit) msg.Bit = new BitBuffer(buf);
        else
        {
            msg.Bytes = new ByteBuffer(buf);
            msg.Bytes.WriteRaw([0xEF, 0xBE, 0xAD, 0xDE, type]);
        }
        return msg;
    }

    public void Send(bool encrypted)
    {
        var buffer = Bit?.Bytes ?? Bytes!.Bytes;
        byte[] frame;
        if (!encrypted)
        {
            frame = new byte[buffer.Length + 6];
            Array.Copy(BitConverter.GetBytes(buffer.Length + 2), frame, 4);
            frame[4] = 0;
            frame[5] = (byte)Type;
            Array.Copy(buffer, 0, frame, 6, buffer.Length);
        }
        else
        {
            var iv = DwCrypto.TigerIv(0x13371337);
            var key = Keys.Get(Connection.Id);
            var crypted = DwCrypto.TripleDesEncrypt(iv, key, buffer);
            frame = new byte[crypted.Length + 9];
            Array.Copy(BitConverter.GetBytes(frame.Length - 4), frame, 4);
            frame[4] = 1;
            Array.Copy(BitConverter.GetBytes(0x13371337), 0, frame, 5, 4);
            Array.Copy(crypted, 0, frame, 9, crypted.Length);
        }
        Connection.Send(frame);
    }
}
