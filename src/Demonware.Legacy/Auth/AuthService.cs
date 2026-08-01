using Demonware.Core;
using Demonware.Core.Crypto;
using Demonware.Core.Store;
using Demonware.Legacy.Protocol;

namespace Demonware.Legacy.Auth;

public sealed class AuthService(FileStore store, TitleId defaultTitle)
{
    public void Handle(LegacyMessage packet, int type)
    {
        switch (type)
        {
            case 28: ClientAuth(packet); break;
            case 12: ServerAuth(packet); break;
            case 26: Iw5KeyCreate(packet); break;
        }
    }

    private void ClientAuth(LegacyMessage packet)
    {
        packet.Bit!.UseDataTypes = false;
        packet.Bit.ReadBoolean(out _);
        packet.Bit.UseDataTypes = true;
        packet.Bit.ReadUInt32(out _);
        packet.Bit.ReadUInt32(out var gameId);
        packet.Bit.ReadUInt32(out var ticketLen);
        packet.Bit.ReadBytes((int)ticketLen, out var ticket);

        try
        {
            var np = TicketFactory.ParseNp(ticket);
            var userId = (int)(np.SteamId & 0xFFFFFFFF);
            if (userId == 0) userId = 1;

            var ivBase = BitConverter.ToUInt32(DwCrypto.RandomBytes(4), 0);
            var iv = DwCrypto.TigerIv(ivBase);
            var key = np.EncryptionKey.Length == 24 ? np.EncryptionKey : DwCrypto.RandomBytes(24);
            var globalKey = DwCrypto.RandomBytes(24);

            var gameTicket = TicketFactory.BuildGameTicket(globalKey, gameId, 0);
            var lsg = TicketFactory.BuildLsgTicket(globalKey, np.SteamId, userId, string.IsNullOrEmpty(np.NickName) ? "Player" : np.NickName);
            var enc = DwCrypto.TripleDesEncrypt(iv, key, gameTicket);

            var reply = packet.MakeReply(29, isBit: true);
            reply.Bit!.UseDataTypes = false;
            reply.Bit.WriteBoolean(false);
            reply.Bit.WriteUInt32(700);
            reply.Bit.WriteUInt32(ivBase);
            reply.Bit.WriteBytes(enc);
            reply.Bit.WriteBytes(lsg);
            reply.Send(encrypted: false);
            Log.Ok("Auth", $"client user={userId} game={gameId}");
        }
        catch (Exception ex) { Log.Error("Auth", ex.Message); }
    }

    private void ServerAuth(LegacyMessage packet)
    {
        packet.Bit!.UseDataTypes = false;
        packet.Bit.ReadBoolean(out _);
        packet.Bit.UseDataTypes = true;
        packet.Bit.ReadUInt32(out _);
        packet.Bit.ReadUInt32(out var gameId);
        var keyBuf = new byte[8];
        packet.Bit.Read(64, keyBuf);
        var keyData = BitConverter.ToUInt64(keyBuf, 0);

        try
        {
            byte[] key24;
            string nick;
            int userId = 1;
            if (defaultTitle == TitleId.Iw5 || gameId == (uint)TitleId.Iw5)
            {
                long hash = unchecked((long)keyData);
                if (!store.TryGetServerKey(hash, out var keyStr, out _))
                    keyStr = "OFFLINE-" + keyData.ToString("X16");
                using var tiger = new Demonware.Core.Crypto.TigerHash();
                key24 = tiger.ComputeHash(System.Text.Encoding.ASCII.GetBytes(keyStr));
                nick = "IW5-Server";
            }
            else
            {
                using var tiger = new Demonware.Core.Crypto.TigerHash();
                key24 = tiger.ComputeHash(BitConverter.GetBytes(keyData));
                if (key24.Length > 24) Array.Resize(ref key24, 24);
                nick = "Server";
            }

            var globalKey = DwCrypto.RandomBytes(24);
            var ivBase = BitConverter.ToUInt32(DwCrypto.RandomBytes(4), 0);
            var iv = DwCrypto.TigerIv(ivBase);
            var gameTicket = TicketFactory.BuildGameTicket(globalKey, gameId, 4);
            var lsg = TicketFactory.BuildLsgTicket(globalKey, keyData, userId, nick);
            var enc = DwCrypto.TripleDesEncrypt(iv, key24, gameTicket);

            var reply = packet.MakeReply(13, isBit: true);
            reply.Bit!.UseDataTypes = false;
            reply.Bit.WriteBoolean(false);
            reply.Bit.WriteUInt32(700);
            reply.Bit.WriteUInt32(ivBase);
            reply.Bit.WriteBytes(enc);
            reply.Bit.WriteBytes(lsg);
            reply.Send(encrypted: false);
            Log.Ok("Auth", $"server game={gameId}");
        }
        catch (Exception ex) { Log.Error("Auth", ex.Message); }
    }

    private void Iw5KeyCreate(LegacyMessage packet)
    {
        // Minimal accept: reply status 700 with random key material
        packet.Bit!.UseDataTypes = false;
        packet.Bit.ReadBoolean(out _);
        packet.Bit.UseDataTypes = true;
        packet.Bit.ReadUInt32(out _);
        packet.Bit.ReadUInt32(out var gameId);

        var keyStr = "X" + Guid.NewGuid().ToString("N")[..19].ToUpperInvariant();
        keyStr = $"{keyStr[0..3]}-{keyStr[3..7]}-{keyStr[7..11]}-{keyStr[11..15]}-{keyStr[15..19]}";
        using var tiger = new Demonware.Core.Crypto.TigerHash();
        var hash = tiger.ComputeHash(System.Text.Encoding.ASCII.GetBytes(keyStr));
        var keyHash = BitConverter.ToInt64(hash, 0);
        store.SaveServerKey(keyHash, keyStr, 0);

        var globalKey = DwCrypto.RandomBytes(24);
        var ivBase = BitConverter.ToUInt32(DwCrypto.RandomBytes(4), 0);
        var iv = DwCrypto.TigerIv(ivBase);
        var encKey = tiger.ComputeHash(System.Text.Encoding.ASCII.GetBytes(keyStr));
        var gameTicket = TicketFactory.BuildGameTicket(globalKey, gameId, 4);
        var lsg = TicketFactory.BuildLsgTicket(globalKey, (ulong)keyHash, 1, "");
        var enc = DwCrypto.TripleDesEncrypt(iv, encKey, gameTicket);

        var reply = packet.MakeReply(25, isBit: true);
        reply.Bit!.UseDataTypes = false;
        reply.Bit.WriteBoolean(false);
        reply.Bit.WriteUInt32(700);
        reply.Bit.WriteUInt32(ivBase);
        reply.Bit.WriteBytes(enc);
        reply.Bit.WriteBytes(lsg);
        var keyStuff = new byte[86];
        var ascii = System.Text.Encoding.ASCII.GetBytes(keyStr);
        Array.Copy(ascii, keyStuff, Math.Min(ascii.Length, 86));
        reply.Bit.WriteBytes(keyStuff);
        // unk int as raw 32 bits without datatype
        reply.Bit.Write(32, BitConverter.GetBytes(0));
        reply.Send(encrypted: false);
        Log.Ok("Auth", "iw5 dedi key created");
    }
}
