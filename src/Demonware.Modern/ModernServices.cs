using System.Text;
using System.Text.RegularExpressions;
using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;

namespace Demonware.Modern;

/// <summary>
/// Union of NewDW + NewDW2 + NewDW3 + NewDW4 lobby services (AES path).
/// Real handlers where upstream has logic; otherwise Empty-OK (same as NewDW stubs).
/// </summary>
public static class ModernServices
{
    private static long _tid = unchecked((long)0x8000000000000000L);
    public static ulong NextTransactionId() => unchecked((ulong)Interlocked.Increment(ref _tid));

    // Service name map for logging (NewDW* union)
    private static readonly Dictionary<byte, string> Names = new()
    {
        [3] = "bdTeams", [4] = "bdStats", [8] = "bdProfiles", [10] = "bdStorage",
        [12] = "bdTitleUtilities", [15] = "bdKeyArchive/ObjectStore", [18] = "bdBandwidthTest",
        [19] = "bdStats2", [21] = "bdMatchMaking", [23] = "bdCounter", [27] = "bdDML",
        [28] = "bdGroup", [29] = "bdCMail", [36] = "bdFacebook", [38] = "bdAnticheat",
        [50] = "bdContentStreaming", [52] = "bdTags", [58] = "bdPooledStorage",
        [63] = "bdUNK63", [65] = "bdUserGroups", [67] = "bdEventLog", [68] = "bdRichPresence",
        [80] = "bdMarketplace", [81] = "bdLeague", [82] = "bdLeague2", [91] = "bdStats3",
        [95] = "bdPublisherVariables", [96] = "bdDDL", [103] = "bdPresence",
        [104] = "bdMarketingComms", [125] = "bdUNK125", [138] = "bdMatchMaking",
        [139] = "bdReward", [145] = "bdAsyncMatchMaking", [193] = "bdObjectStore",
        [195] = "bdLootGeneration"
    };

    public static void Dispatch(TcpConnection conn, ModernSession session, FileStore store, byte serviceId, byte[] serviceData)
    {
        if (serviceData.Length < 1) { session.SendEmptyOk(conn, 0); return; }
        var taskId = serviceData[0];
        var payload = serviceData.AsSpan(1).ToArray();
        var name = Names.TryGetValue(serviceId, out var n) ? n : $"svc{serviceId}";
        Log.Debug("Modern", $"{name} task={taskId} ({payload.Length}b)");

        try
        {
            switch (serviceId)
            {
                case 18:
                    session.SendServiceReply(conn, BandwidthBlob);
                    return;

                case 27:
                    if (taskId == 2) { SendDml(conn, session, taskId); return; }
                    break;

                case 12:
                    if (taskId == 6) { SendTime(conn, session, taskId); return; }
                    if (taskId == 1)
                    {
                        ReplyOk(conn, session, taskId, w => { w.Write((uint)1); w.Write((uint)1); w.Write((uint)0); });
                        return;
                    }
                    break;

                case 10:
                case 58:
                    if (ModernStorage.Handle(conn, session, store, taskId, payload)) return;
                    break;

                case 21:
                case 138:
                case 145:
                    if (ModernMatchMaking.Handle(conn, session, taskId, payload)) return;
                    break;

                case 8:
                    if (ModernProfiles.Handle(conn, session, store, taskId, payload)) return;
                    break;

                case 67:
                    if (ModernEventLog.Handle(conn, session, store, taskId, payload)) return;
                    break;

                // Registered Empty-OK services (parity with NewDW* register lists)
                case 3: case 4: case 15: case 19: case 23: case 28: case 29:
                case 36: case 38: case 50: case 52: case 63: case 65: case 68:
                case 80: case 81: case 82: case 91: case 95: case 96:
                case 103: case 104: case 125: case 139: case 193: case 195:
                    break;

                default:
                    Log.Debug("Modern", $"unregistered service {serviceId}");
                    break;
            }
        }
        catch (Exception ex) { Log.Error("Modern", $"{name}: {ex.Message}"); }

        session.SendEmptyOk(conn, taskId);
    }

    internal static void ReplyOk(TcpConnection conn, ModernSession session, byte taskId, Action<BinaryWriter>? extra = null)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(NextTransactionId());
        bw.Write((uint)0);
        bw.Write(taskId);
        if (extra != null) extra(bw);
        else { bw.Write((uint)0); bw.Write((uint)0); }
        session.SendServiceReply(conn, ms.ToArray());
    }

    internal static void ReplyError(TcpConnection conn, ModernSession session, byte taskId, uint error)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(NextTransactionId());
        bw.Write(error);
        bw.Write(taskId);
        session.SendServiceReply(conn, ms.ToArray());
    }

    private static readonly byte[] BandwidthBlob =
    [
        0x0F,0xC1,0x1C,0x37,0xB8,0xEF,0x7C,0xD6,0x00,0x00,0x04,0x00,0x00,0x00,0x04,0x00,0x00,0xF4,0x01,0x00,0x00,0xD0,0x07,
        0x00,0x00,0x10,0x27,0x00,0x00,0x88,0x13,0x00,0x00,0xF4,0x01,0x00,0x00,0x02,0x0C,0x88,0xB3,0x04,0x65,0x89,0xBF,0xC3,0x6A,0x27,0x94,0xD4,0x8F
    ];

    private static void SendDml(TcpConnection conn, ModernSession session, byte taskId)
    {
        ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)1); bw.Write((uint)1);
            WriteZ(bw, "US"); WriteZ(bw, "United States of America");
            WriteZ(bw, "New York"); WriteZ(bw, "New York");
            bw.Write(0f); bw.Write(0f); bw.Write((uint)0x2119); WriteZ(bw, "+01:00");
        });
    }

    private static void SendTime(TcpConnection conn, ModernSession session, byte taskId)
    {
        ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)1); bw.Write((uint)1);
            bw.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        });
    }

    internal static void WriteZ(BinaryWriter bw, string s)
    {
        bw.Write(Encoding.ASCII.GetBytes(s ?? ""));
        bw.Write((byte)0);
    }

    internal static void WriteBlob(BinaryWriter bw, byte[] data)
    {
        data ??= [];
        bw.Write((uint)data.Length);
        bw.Write(data);
    }

    internal static List<string> ExtractStrings(byte[] payload)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (var b in payload)
        {
            if (b is >= 32 and < 127) sb.Append((char)b);
            else if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }
}

/// <summary>NewDW bdStorage (10) / NewDW2 bdPooledStorage (58).</summary>
public static class ModernStorage
{
    private const uint BdNoFile = 0x3E8;

    public static bool Handle(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        return taskId switch
        {
            20 => ListPublisher(conn, session, store, taskId, payload),
            21 or 7 => GetPublisher(conn, session, store, taskId, payload),
            18 or 24 => Upload(conn, session, store, taskId, payload),
            16 => GetFiles(conn, session, store, taskId, payload),
            12 or 3 => GetFile(conn, session, store, taskId, payload),
            _ => false
        };
    }

    private static bool ListPublisher(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        var strings = ModernServices.ExtractStrings(payload);
        var filter = strings.LastOrDefault() ?? "";
        var files = Directory.Exists(store.PubDir) ? Directory.GetFiles(store.PubDir) : [];
        if (!string.IsNullOrEmpty(filter))
        {
            try
            {
                var rx = new Regex("^" + Regex.Escape(filter).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
                files = files.Where(f => rx.IsMatch(Path.GetFileName(f))).ToArray();
            }
            catch
            {
                files = files.Where(f => Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
        }

        ModernServices.ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)files.Length);
            bw.Write((uint)files.Length);
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                var len = new FileInfo(path).Length;
                ModernServices.WriteZ(bw, name);
                bw.Write((uint)len);
                bw.Write((ulong)0);
            }
        });
        return true;
    }

    private static bool GetPublisher(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        var strings = ModernServices.ExtractStrings(payload);
        var filename = strings.Count >= 2 ? strings[1] : strings.FirstOrDefault();
        if (string.IsNullOrEmpty(filename))
        {
            ModernServices.ReplyError(conn, session, taskId, BdNoFile);
            return true;
        }

        var path = Path.Combine(store.PubDir, filename);
        if (!File.Exists(path))
        {
            Log.Debug("Modern", $"publisher miss: {filename}");
            ModernServices.ReplyError(conn, session, taskId, BdNoFile);
            return true;
        }

        var data = File.ReadAllBytes(path);
        Log.Ok("Modern", $"publisher {filename} ({data.Length}b)");
        ModernServices.ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)1); bw.Write((uint)1);
            ModernServices.WriteBlob(bw, data);
        });
        return true;
    }

    private static bool Upload(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        // Best-effort: pull printable filenames + trailing blob-ish regions
        var strings = ModernServices.ExtractStrings(payload);
        var filename = strings.Skip(1).FirstOrDefault() ?? strings.FirstOrDefault() ?? $"upload_{DateTime.UtcNow.Ticks}";
        // raw payload after last string as data
        var data = payload;
        store.SaveFile(filename, 1, data);
        Log.Ok("Modern", $"user file set: {filename} ({data.Length}b)");
        ModernServices.ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)1); bw.Write((uint)1);
            ModernServices.WriteZ(bw, filename);
            bw.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        });
        return true;
    }

    private static bool GetFiles(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        var names = ModernServices.ExtractStrings(payload);
        var found = new List<(string name, byte[] data)>();
        foreach (var name in names)
        {
            var bytes = store.GetFile(name);
            if (bytes != null) found.Add((name, bytes));
        }

        ModernServices.ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)found.Count); bw.Write((uint)found.Count);
            foreach (var (name, data) in found)
            {
                bw.Write((ulong)1);
                ModernServices.WriteZ(bw, "steam");
                ModernServices.WriteZ(bw, name);
                bw.Write((uint)0);
                ModernServices.WriteBlob(bw, data);
            }
        });
        return true;
    }

    private static bool GetFile(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        var name = ModernServices.ExtractStrings(payload).FirstOrDefault();
        if (string.IsNullOrEmpty(name))
        {
            ModernServices.ReplyError(conn, session, taskId, BdNoFile);
            return true;
        }
        var bytes = store.GetFile(name);
        if (bytes == null)
        {
            ModernServices.ReplyError(conn, session, taskId, BdNoFile);
            return true;
        }
        ModernServices.ReplyOk(conn, session, taskId, bw =>
        {
            bw.Write((uint)1); bw.Write((uint)1);
            ModernServices.WriteBlob(bw, bytes);
        });
        return true;
    }
}

/// <summary>NewDW bdMatchMaking 138 / BO3 21 / Async 145.</summary>
public static class ModernMatchMaking
{
    public static bool Handle(TcpConnection conn, ModernSession session, byte taskId, byte[] payload)
    {
        switch (taskId)
        {
            case 1: // createSession
                ModernServices.ReplyOk(conn, session, taskId, bw =>
                {
                    bw.Write((uint)1); bw.Write((uint)1);
                    ulong sid = (ulong)Environment.TickCount64 ^ (ulong)DateTime.UtcNow.Ticks;
                    bw.Write(sid);
                });
                return true;
            case 10: // getPerformanceValues
                ModernServices.ReplyOk(conn, session, taskId, bw =>
                {
                    bw.Write((uint)1); bw.Write((uint)1);
                    bw.Write((ulong)1);
                    bw.Write(10.0f);
                });
                return true;
            case 2: case 3: case 4: case 5: case 6: case 8: case 9:
            case 11: case 12: case 13: case 14: case 15: case 16:
                ModernServices.ReplyOk(conn, session, taskId);
                return true;
            default:
                return false;
        }
    }
}

public static class ModernProfiles
{
    public static bool Handle(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        switch (taskId)
        {
            case 1: // getPublicInfos — return empty list or stored
            {
                var ids = new List<int>();
                // u64 entity ids often typed as 0x0A; extract any 8-byte aligned leftovers via strings fallback
                for (int i = 0; i + 8 <= payload.Length; i++)
                {
                    // skip — Empty list is valid when no profiles
                }
                ModernServices.ReplyOk(conn, session, taskId, bw =>
                {
                    bw.Write((uint)0); bw.Write((uint)0);
                });
                return true;
            }
            case 3: // setPublicInfo
            {
                store.SaveProfile(1, 0, payload);
                ModernServices.ReplyOk(conn, session, taskId);
                return true;
            }
            case 2: case 4: case 5: case 6: case 7: case 8:
                ModernServices.ReplyOk(conn, session, taskId);
                return true;
            default:
                return false;
        }
    }
}

public static class ModernEventLog
{
    public static bool Handle(TcpConnection conn, ModernSession session, FileStore store, byte taskId, byte[] payload)
    {
        if (taskId == 2)
        {
            store.AppendEvent(2, payload);
            ModernServices.ReplyOk(conn, session, taskId);
            return true;
        }
        return false;
    }
}

public sealed class ModernLobbyHandler(FileStore store) : IConnectionHandler
{
    public void OnConnected(TcpConnection connection) => connection.State = new ModernSession(store);
    public void OnData(TcpConnection connection, byte[] data)
    {
        if (connection.State is ModernSession s) s.Handle(connection, data);
    }
    public void OnDisconnected(TcpConnection connection) => connection.State = null;
}
