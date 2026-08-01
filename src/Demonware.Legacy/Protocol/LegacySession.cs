using Demonware.Core;
using Demonware.Core.Crypto;
using Demonware.Core.Net;
using Demonware.Core.Store;
using Demonware.Legacy.Auth;

namespace Demonware.Legacy.Protocol;

public sealed class LegacySession
{
    private readonly SessionKeyMap _keys;
    private readonly AuthService _auth;
    private readonly FileStore _store;
    private int _bytesRead;
    private int _totalBytes;
    private MemoryStream? _messageBuffer;

    public LegacySession(SessionKeyMap keys, FileStore store, TitleId title)
    {
        _keys = keys;
        _store = store;
        _auth = new AuthService(store, title);
    }

    public void Handle(TcpConnection conn, byte[] buffer)
    {
        using var stream = new MemoryStream(buffer);
        using var reader = new BinaryReader(stream);
        try
        {
            while (stream.Position < stream.Length)
            {
                if (_bytesRead == 0)
                {
                    try { _totalBytes = reader.ReadInt32(); }
                    catch { _totalBytes = 0; }

                    if (_totalBytes == 0xC8) { _totalBytes = 0; break; }
                    if (_totalBytes is > 256 * 1024 or <= 0)
                    {
                        if (_totalBytes <= 0) conn.Send(new byte[4]);
                        break;
                    }
                    _messageBuffer = new MemoryStream();
                }

                var len = Math.Min((int)(buffer.Length - stream.Position), _totalBytes - _bytesRead);
                // re-read from current position correctly:
                var available = (int)(stream.Length - stream.Position);
                len = Math.Min(available, _totalBytes - _bytesRead);
                var chunk = reader.ReadBytes(len);
                _messageBuffer!.Write(chunk, 0, chunk.Length);
                _bytesRead += chunk.Length;

                if (_bytesRead < _totalBytes) continue;

                _bytesRead = 0;
                _messageBuffer.Position = 0;
                using var breader = new BinaryReader(_messageBuffer);
                var remaining = _totalBytes;
                remaining--;
                var pdtype = breader.ReadByte();
                if (pdtype == 0xFF) continue;

                int ptype;
                byte[] pdata;
                var encrypted = pdtype == 1;

                if (!encrypted)
                {
                    ptype = breader.ReadByte();
                    remaining--;
                    pdata = breader.ReadBytes(remaining);
                }
                else
                {
                    var key = _keys.Get(conn.Id);
                    var iv = DwCrypto.TigerIv(breader.ReadUInt32());
                    var edata = breader.ReadBytes(remaining - 4);
                    var ddata = DwCrypto.TripleDesDecrypt(iv, key, edata);
                    using var ds = new MemoryStream(ddata);
                    using var dr = new BinaryReader(ds);
                    _ = dr.ReadUInt32();
                    ptype = dr.ReadByte();
                    pdata = dr.ReadBytes((int)(ds.Length - 5));
                }

                Dispatch(conn, encrypted, ptype, pdata);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Legacy", ex.ToString());
        }
    }

    private void Dispatch(TcpConnection conn, bool crypt, int type, byte[] pdata)
    {
        var msg = LegacyMessage.FromRequest(conn, _keys, pdata, type);
        try
        {
            if (!crypt && type is 28 or 12 or 26) { _auth.Handle(msg, type); return; }
            if (type == 7) { InstallSession(conn, msg); return; }
            if (crypt) { ReplyEmptyOk(msg, type); return; }
        }
        catch (Exception ex)
        {
            Log.Error("Legacy", ex.Message);
            if (crypt) ReplyUnknown(msg);
        }
    }

    private void InstallSession(TcpConnection conn, LegacyMessage packet)
    {
        packet.Bit!.UseDataTypes = false;
        packet.Bit.ReadBoolean(out _);
        packet.Bit.UseDataTypes = true;
        packet.Bit.ReadUInt32(out var gameId);
        packet.Bit.ReadUInt32(out _);
        packet.Bit.ReadBytes(128, out var ticket);
        var key = TicketFactory.KeyFromLsg(ticket);
        _keys.Set(conn.Id, key);
        Log.Ok("Lobby", $"session key installed title={gameId} user={TicketFactory.NameFromLsg(ticket)}");
    }

    private static void ReplyEmptyOk(LegacyMessage packet, int subtype)
    {
        var reply = packet.MakeReply(1, isBit: false);
        reply.Bytes!.Write(0x8000000000000001UL);
        reply.Bytes.Write((uint)0);
        reply.Bytes.Write((byte)subtype);
        reply.Bytes.Write((uint)0);
        reply.Bytes.Write((uint)0);
        reply.Send(encrypted: true);
    }

    private static void ReplyUnknown(LegacyMessage packet)
    {
        var reply = packet.MakeReply(1, isBit: false);
        reply.Bytes!.Write(0x8000000000000001UL);
        reply.Bytes.Write((uint)2);
        reply.Send(encrypted: true);
    }
}

public sealed class LegacyLobbyHandler(SessionKeyMap keys, FileStore store, TitleId title = TitleId.Iw6) : IConnectionHandler
{
    public void OnConnected(TcpConnection connection) =>
        connection.State = new LegacySession(keys, store, title);

    public void OnData(TcpConnection connection, byte[] data)
    {
        if (connection.State is LegacySession session)
            session.Handle(connection, data);
    }

    public void OnDisconnected(TcpConnection connection)
    {
        keys.Remove(connection.Id);
        connection.State = null;
    }
}
