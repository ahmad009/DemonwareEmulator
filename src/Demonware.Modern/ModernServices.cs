using System.Text;
using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;

namespace Demonware.Modern;

public static class ModernServices
{
    private static long _tid = unchecked((long)0x8000000000000000L);
    public static ulong NextTransactionId() => unchecked((ulong)Interlocked.Increment(ref _tid));

    public static void Dispatch(TcpConnection conn, ModernSession session, FileStore store, byte serviceId, byte[] serviceData)
    {
        if (serviceData.Length < 1) { session.SendEmptyOk(conn, 0); return; }
        var taskId = serviceData[0];
        var payload = serviceData.AsSpan(1).ToArray();
        Log.Debug("Modern", $"service {serviceId} task {taskId} ({payload.Length}b)");
        try
        {
            switch (serviceId)
            {
                case 18: session.SendServiceReply(conn, BandwidthBlob); return;
                case 27 when taskId == 2: SendDml(conn, session, taskId); return;
                case 12 when taskId == 6: SendTime(conn, session, taskId); return;
                case 12 when taskId == 1:
                {
                    using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
                    bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1); bw.Write((uint)0);
                    session.SendServiceReply(conn, ms.ToArray()); return;
                }
                case 10: case 58:
                    if (TryStorage(conn, session, store, taskId, payload)) return; break;
                case 21: case 138: case 145:
                    if (taskId == 1) { SendSessionId(conn, session, taskId); return; } break;
            }
        }
        catch (Exception ex) { Log.Error("Modern", ex.Message); }
        session.SendEmptyOk(conn, taskId);
    }

    private static readonly byte[] BandwidthBlob = [
        0x0F,0xC1,0x1C,0x37,0xB8,0xEF,0x7C,0xD6,0x00,0x00,0x04,0x00,0x00,0x00,0x04,0x00,0x00,0xF4,0x01,0x00,0x00,0xD0,0x07,
        0x00,0x00,0x10,0x27,0x00,0x00,0x88,0x13,0x00,0x00,0xF4,0x01,0x00,0x00,0x02,0x0C,0x88,0xB3,0x04,0x65,0x89,0xBF,0xC3,0x6A,0x27,0x94,0xD4,0x8F
    ];

    private static void SendSessionId(TcpConnection conn, ModernSession session, byte taskId)
    {
        ulong sid = (ulong)Environment.TickCount64 ^ (ulong)DateTime.UtcNow.Ticks;
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1); bw.Write(sid);
        session.SendServiceReply(conn, ms.ToArray());
    }

    private static void SendDml(TcpConnection conn, ModernSession session, byte taskId)
    {
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1);
        WriteZ(bw,"US"); WriteZ(bw,"United States of America"); WriteZ(bw,"New York"); WriteZ(bw,"New York");
        bw.Write(0f); bw.Write(0f); bw.Write((uint)0x2119); WriteZ(bw,"+01:00");
        session.SendServiceReply(conn, ms.ToArray());
    }

    private static void SendTime(TcpConnection conn, ModernSession session, byte taskId)
    {
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1);
        bw.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        session.SendServiceReply(conn, ms.ToArray());
    }

    private static bool TryStorage(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        if (taskId is 21 or 7)
        {
            var filename = PickString(payload, 2) ?? PickString(payload, 1);
            if (!string.IsNullOrEmpty(filename))
            {
                var path = Path.Combine(store.PubDir, filename);
                if (File.Exists(path))
                {
                    var fileData = File.ReadAllBytes(path);
                    using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
                    bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1);
                    bw.Write((uint)fileData.Length); bw.Write(fileData);
                    session.SendServiceReply(conn, ms.ToArray()); return true;
                }
            }
        }
        if (taskId is 12 or 3)
        {
            var name = PickString(payload, 1);
            if (!string.IsNullOrEmpty(name))
            {
                var bytes = store.GetFile(name);
                if (bytes is not null)
                {
                    using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
                    bw.Write(NextTransactionId()); bw.Write((uint)0); bw.Write(taskId); bw.Write((uint)1); bw.Write((uint)1);
                    bw.Write((uint)bytes.Length); bw.Write(bytes);
                    session.SendServiceReply(conn, ms.ToArray()); return true;
                }
            }
        }
        return false;
    }

    private static string? PickString(byte[] payload, int which)
    {
        var parts = new List<string>(); var sb = new StringBuilder();
        foreach (var b in payload)
        {
            if (b is >= 32 and < 127) sb.Append((char)b);
            else if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        if (parts.Count >= which) return parts[which - 1];
        return parts.Count > 0 ? parts[0] : null;
    }

    private static void WriteZ(BinaryWriter bw, string s) { bw.Write(Encoding.ASCII.GetBytes(s)); bw.Write((byte)0); }
}

public sealed class ModernLobbyHandler(FileStore store) : IConnectionHandler
{
    public void OnConnected(TcpConnection connection) => connection.State = new ModernSession(store);
    public void OnData(TcpConnection connection, byte[] data) { if (connection.State is ModernSession s) s.Handle(connection, data); }
    public void OnDisconnected(TcpConnection connection) => connection.State = null;
}
