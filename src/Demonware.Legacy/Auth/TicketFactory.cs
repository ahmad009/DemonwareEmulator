using System.Text;

namespace Demonware.Legacy.Auth;

public sealed class NpTicket
{
    public string SessionId { get; init; } = "";
    public byte[] EncryptionKey { get; init; } = new byte[24];
    public string NickName { get; init; } = "Player";
    public ulong SteamId { get; init; }
}

public static class TicketFactory
{
    public static NpTicket ParseNp(byte[] data)
    {
        if (data.Length < 128) Array.Resize(ref data, 128);
        var key = new byte[24];
        Array.Copy(data, 32, key, 0, 24);
        return new NpTicket
        {
            SessionId = Encoding.ASCII.GetString(data, 0, 32).Trim('\0'),
            EncryptionKey = key,
            NickName = Encoding.ASCII.GetString(data, 56, 64).Trim('\0'),
            SteamId = BitConverter.ToUInt64(data, 120)
        };
    }

    public static byte[] BuildGameTicket(byte[] cryptoKey24, uint gameId, byte licenseType)
    {
        if (cryptoKey24.Length != 24) throw new ArgumentException("key");
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { 0xDE, 0xAD, 0xBD, 0xEF });
        w.Write(licenseType);
        w.Write(gameId);
        w.Write(Enumerable.Repeat((byte)0x0A, 16).ToArray());
        w.Write(unchecked((long)0x01100001DEADC0DE));
        w.Write(Encoding.ASCII.GetBytes("valuableAsset3".PadRight(64, '\0')));
        w.Write(cryptoKey24);
        w.Write(Enumerable.Repeat((byte)0x0A, 7).ToArray());
        return ms.ToArray();
    }

    public static byte[] BuildLsgTicket(byte[] cryptoKey24, ulong npid, int userId, string nickname)
    {
        var bytes = new byte[128];
        Array.Copy(cryptoKey24, bytes, 24);
        Array.Copy(BitConverter.GetBytes(npid), 0, bytes, 24, 8);
        Array.Copy(BitConverter.GetBytes(userId), 0, bytes, 32, 4);
        var nick = Encoding.UTF8.GetBytes(nickname ?? "Player");
        Array.Copy(nick, 0, bytes, 36, Math.Min(nick.Length, 64));
        return bytes;
    }

    public static byte[] KeyFromLsg(byte[] t) { var k = new byte[24]; Array.Copy(t, k, 24); return k; }
    public static ulong IdFromLsg(byte[] t) => BitConverter.ToUInt64(t, 24);
    public static int UserFromLsg(byte[] t) => BitConverter.ToInt32(t, 32);
    public static string NameFromLsg(byte[] t) => Encoding.UTF8.GetString(t, 36, 64).TrimEnd('\0');
}
