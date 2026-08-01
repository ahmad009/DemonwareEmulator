using System;
using System.Collections.Generic;
using System.Linq;

namespace DWServer
{
    /// <summary>
    /// Legacy bdStats — accepts BOTH service ids:
    ///   4  = Demonware\reference (IW6 dump): tasks 1 write, 3 byEntity, 4 byRank, 5 byPivot
    ///   19 = Demonware\src:                 tasks 1 write, 2 byEntity, 3 byRank, 4 byPivot
    /// Legacy-only; not wired into Modern AES lobby.
    /// </summary>
    public static class DWStats
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<uint, Dictionary<ulong, long>> Boards =
            new Dictionary<uint, Dictionary<ulong, long>>();

        private struct Row
        {
            public ulong EntityId;
            public long Rating;
            public ulong Rank;
            public string Name;
            public uint SecondsSinceUpdate;
        }

        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            if (type != 4 && type != 19) return;

            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            // src (19) vs reference (4) use different task numbers for the same ops
            var useSrcTasks = (type == 19);

            try
            {
                if (useSrcTasks)
                {
                    switch (call)
                    {
                        case 1: WriteStatsSrc(data, packet, call); break;
                        case 2: ReadByEntitySrc(data, packet, call); break;
                        case 3: ReadByRankSrc(data, packet, call); break;
                        case 4: ReadByPivot(data, packet, call); break;
                        default:
                            Log.Debug("unknown packet " + call + " in bdStats(19/src)");
                            EmptyOk(packet, call);
                            break;
                    }
                }
                else
                {
                    switch (call)
                    {
                        case 1: WriteStatsRef(data, packet, call); break;
                        case 3: ReadByEntityRef(data, packet, call); break;
                        case 4: ReadByRankRef(data, packet, call); break;
                        case 5: ReadByPivot(data, packet, call); break;
                        default:
                            Log.Debug("unknown packet " + call + " in bdStats(4/ref)");
                            EmptyOk(packet, call);
                            break;
                    }
                }
                data.Arguments["handled"] = true;
            }
            catch (Exception e)
            {
                Log.Error("DWStats: " + e);
                EmptyOk(packet, call);
                data.Arguments["handled"] = true;
            }
        }

        private static void WriteStatsRef(MessageData data, DWMessage packet, byte call)
        {
            // bdStatsInfo::serialize: u32 lb, u64 entity, u8 writeType, i64 rating
            var n = 0;
            lock (Gate)
            {
                while (packet.ByteBuffer.RemainingBytes >= 4)
                {
                    uint lb;
                    try { lb = packet.ByteBuffer.ReadUInt32(); }
                    catch { break; }
                    if (lb == 0xFFFFFFFFu) break;

                    var entity = packet.ByteBuffer.ReadUInt64();
                    var writeType = packet.ByteBuffer.ReadByte();
                    var rating = packet.ByteBuffer.ReadInt64();
                    ApplyWrite(lb, entity, writeType == 1, rating);
                    n++;
                }
            }
            Log.Info("bdStats(4) write rows=" + n);
            EmptyOk(packet, call);
        }

        private static void WriteStatsSrc(MessageData data, DWMessage packet, byte call)
        {
            // Demonware\src: count, then rows (lb, entity, writeType u32, colCount, cols as u64)
            var count = packet.ByteBuffer.ReadUInt32();
            lock (Gate)
            {
                for (uint i = 0; i < count; i++)
                {
                    var lb = packet.ByteBuffer.ReadUInt32();
                    var entity = packet.ByteBuffer.ReadUInt64();
                    var writeType = packet.ByteBuffer.ReadUInt32();
                    var colCount = packet.ByteBuffer.ReadUInt32();
                    long rating = 0;
                    for (uint c = 0; c < colCount; c++)
                    {
                        var v = (long)packet.ByteBuffer.ReadUInt64();
                        if (c == 0) rating = v;
                    }
                    ApplyWrite(lb, entity, writeType == 1, rating);
                }
            }
            Log.Info("bdStats(19) write count=" + count);
            EmptyOk(packet, call);
        }

        private static void ApplyWrite(uint lb, ulong entity, bool increment, long rating)
        {
            if (!Boards.ContainsKey(lb))
                Boards[lb] = new Dictionary<ulong, long>();
            if (increment && Boards[lb].ContainsKey(entity))
                Boards[lb][entity] += rating;
            else
                Boards[lb][entity] = rating;
        }

        private static void ReadByEntityRef(MessageData data, DWMessage packet, byte call)
        {
            var lb = packet.ByteBuffer.ReadUInt32();
            var entities = new List<ulong>();
            while (packet.ByteBuffer.RemainingBytes >= 9)
            {
                try { entities.Add(packet.ByteBuffer.ReadUInt64()); }
                catch { break; }
            }
            SendEntityRows(packet, call, lb, entities);
        }

        private static void ReadByEntitySrc(MessageData data, DWMessage packet, byte call)
        {
            var lb = packet.ByteBuffer.ReadUInt32();
            var count = packet.ByteBuffer.ReadUInt32();
            var entities = new List<ulong>();
            for (uint i = 0; i < count; i++)
                entities.Add(packet.ByteBuffer.ReadUInt64());
            SendEntityRows(packet, call, lb, entities);
        }

        private static void SendEntityRows(DWMessage packet, byte call, uint lb, List<ulong> entities)
        {
            var rows = new List<Row>();
            lock (Gate)
            {
                foreach (var entity in entities)
                {
                    long rating = 0;
                    if (Boards.ContainsKey(lb))
                        Boards[lb].TryGetValue(entity, out rating);
                    rows.Add(MakeRow(entity, rating));
                }
            }
            SendRows(packet, call, rows);
        }

        private static void ReadByRankRef(MessageData data, DWMessage packet, byte call)
        {
            var lb = packet.ByteBuffer.ReadUInt32();
            var firstRank = packet.ByteBuffer.ReadUInt64();
            var maxResults = packet.ByteBuffer.ReadUInt32();
            if (maxResults == 0 || maxResults > 100) maxResults = 20;
            var start = (int)Math.Max(0UL, firstRank > 0 ? firstRank - 1 : 0);
            List<Row> rows;
            lock (Gate) { rows = Ranked(lb).Skip(start).Take((int)maxResults).ToList(); }
            SendRows(packet, call, rows);
        }

        private static void ReadByRankSrc(MessageData data, DWMessage packet, byte call)
        {
            var lb = packet.ByteBuffer.ReadUInt32();
            var topRank = packet.ByteBuffer.ReadUInt32();
            var maxResults = packet.ByteBuffer.ReadUInt32();
            if (maxResults == 0 || maxResults > 100) maxResults = 20;
            var start = (int)(topRank > 0 ? topRank - 1 : 0);
            List<Row> rows;
            lock (Gate) { rows = Ranked(lb).Skip(start).Take((int)maxResults).ToList(); }
            SendRows(packet, call, rows);
        }

        private static void ReadByPivot(MessageData data, DWMessage packet, byte call)
        {
            var lb = packet.ByteBuffer.ReadUInt32();
            var entity = packet.ByteBuffer.ReadUInt64();
            var maxResults = packet.ByteBuffer.ReadUInt32();
            if (maxResults == 0 || maxResults > 100) maxResults = 20;

            List<Row> rows;
            lock (Gate)
            {
                var ordered = Ranked(lb);
                var idx = ordered.FindIndex(r => r.EntityId == entity);
                if (idx < 0)
                    rows = ordered.Take((int)maxResults).ToList();
                else
                {
                    var start = Math.Max(0, idx - (int)maxResults / 2);
                    rows = ordered.Skip(start).Take((int)maxResults).ToList();
                }
            }
            SendRows(packet, call, rows);
        }

        private static List<Row> Ranked(uint lb)
        {
            if (!Boards.ContainsKey(lb))
                return new List<Row>();
            return Boards[lb]
                .OrderByDescending(kv => kv.Value)
                .Select((kv, i) => MakeRow(kv.Key, kv.Value, (ulong)(i + 1)))
                .ToList();
        }

        private static Row MakeRow(ulong entity, long rating, ulong rank = 0)
        {
            return new Row
            {
                EntityId = entity,
                Rating = rating,
                Rank = rank,
                Name = entity.ToString("x"),
                SecondsSinceUpdate = 0
            };
        }

        private static void SendRows(DWMessage packet, byte call, List<Row> rows)
        {
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write(call);
            reply.ByteBuffer.Write((uint)rows.Count);
            reply.ByteBuffer.Write((uint)rows.Count);
            foreach (var r in rows)
            {
                reply.ByteBuffer.Write(r.EntityId);
                reply.ByteBuffer.Write(r.Rating);
                reply.ByteBuffer.Write(r.Rank);
                reply.ByteBuffer.Write(r.Name ?? "");
                reply.ByteBuffer.Write(r.SecondsSinceUpdate);
            }
            reply.Send(true);
        }

        private static void EmptyOk(DWMessage packet, byte call)
        {
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write(call);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
        }
    }
}