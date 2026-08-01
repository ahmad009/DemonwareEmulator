using System;
using System.Collections.Concurrent;
using Demonware.Core.Net;

namespace DWServer
{
    public static class TCPHandler
    {
        private static readonly ConcurrentDictionary<string, TcpConnection> Clients = new(StringComparer.Ordinal);

        public static void Register(TcpConnection conn) => Clients[conn.Id] = conn;
        public static void Unregister(TcpConnection conn) => Clients.TryRemove(conn.Id, out _);

        public static void Net_TcpSend(MessageData data)
        {
            try
            {
                var buffer = data.Get<byte[]>("data");
                var cid = data.Get<string>("cid");
                if (buffer == null || cid == null) return;
                if (Clients.TryGetValue(cid, out var conn))
                    conn.Send(buffer);
            }
            catch { }
        }

        public static void ForceDisconnect(MessageData data)
        {
            try
            {
                var cid = data.Get<string>("cid");
                if (cid != null && Clients.TryGetValue(cid, out var conn))
                {
                    try { conn.Socket.Dispose(); } catch { }
                    Unregister(conn);
                }
            }
            catch { }
        }
    }
}
