using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;
using DWServer;

namespace Demonware.Legacy.Protocol;

public sealed class LegacyLobbyHandler : IConnectionHandler
{
    public LegacyLobbyHandler(FileStore store, TitleId title = TitleId.Iw6)
    {
        LocalStore.Backend = store;
        Program.Game = title switch
        {
            TitleId.T5 => TitleID.T5,
            TitleId.Iw5 => TitleID.IW5,
            _ => TitleID.IW6
        };
        DWRouter.OnStart();
    }

    public void OnConnected(TcpConnection connection)
    {
        TCPHandler.Register(connection);
        connection.State = new DWRouter();
    }

    public void OnData(TcpConnection connection, byte[] data)
    {
        if (connection.State is not DWRouter router) return;
        var packet = new MessageData("none");
        packet["data"] = data;
        packet["cid"] = connection.Id;
        packet["source"] = connection.Socket.RemoteEndPoint;
        packet["time"] = DateTime.Now;
        router.handlePacket(packet);
    }

    public void OnDisconnected(TcpConnection connection)
    {
        var packet = new MessageData("none");
        packet["cid"] = connection.Id;
        try { DWMatch.Net_TcpDisconnected(packet); } catch { }
        try { DWGroups.Net_TcpDisconnected(packet); } catch { }
        DWRouter.Net_TcpDisconnected(packet);
        TCPHandler.Unregister(connection);
        connection.State = null;
    }
}
