using System;
using System.Collections.Generic;

namespace DWServer
{
    /// <summary>bdCounter service 0x17 (23) — Demonware\reference\src\bd\bdCounter.cpp</summary>
    public static class DWCounter
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<uint, long> Totals = new Dictionary<uint, long>();

        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 23) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            try
            {
                switch (call)
                {
                    case 1: IncrementCounters(data, packet); break;
                    case 2: GetCounterTotals(data, packet); break;
                    default: EmptyOk(packet, call); break;
                }
            }
            catch (Exception e)
            {
                Log.Error("DWCounter: " + e);
                EmptyOk(packet, call);
            }
        }

        private static void IncrementCounters(MessageData data, DWMessage packet)
        {
            var n = 0;
            lock (Gate)
            {
                while (packet.ByteBuffer.RemainingBytes >= 4)
                {
                    uint id;
                    try { id = packet.ByteBuffer.ReadUInt32(); }
                    catch { break; }
                    if (id == 0xFFFFFFFFu) break;
                    long delta = 1;
                    try { delta = packet.ByteBuffer.ReadInt64(); } catch { }
                    if (!Totals.ContainsKey(id)) Totals[id] = 0;
                    Totals[id] += delta;
                    n++;
                }
            }
            Log.Info("bdCounter increment rows=" + n);
            EmptyOk(packet, 1);
            data.Arguments["handled"] = true;
        }

        private static void GetCounterTotals(MessageData data, DWMessage packet)
        {
            var ids = new List<uint>();
            while (packet.ByteBuffer.RemainingBytes >= 4)
            {
                try
                {
                    var id = packet.ByteBuffer.ReadUInt32();
                    if (id == 0xFFFFFFFFu) break;
                    ids.Add(id);
                }
                catch { break; }
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)2);
            reply.ByteBuffer.Write((uint)ids.Count);
            reply.ByteBuffer.Write((uint)ids.Count);
            lock (Gate)
            {
                foreach (var id in ids)
                {
                    long v = 0;
                    Totals.TryGetValue(id, out v);
                    reply.ByteBuffer.Write(id);
                    reply.ByteBuffer.Write(v);
                }
            }
            reply.Send(true);
            data.Arguments["handled"] = true;
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