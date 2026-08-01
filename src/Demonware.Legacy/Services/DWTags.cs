using System;
using System.Collections.Generic;

namespace DWServer
{
    /// <summary>bdTags service 0x34 (52) — Demonware\reference\src\bd\bdTags.cpp</summary>
    public static class DWTags
    {
        private static readonly object Gate = new object();
        // collection -> entity -> list of (pri,sec)
        private static readonly Dictionary<uint, Dictionary<ulong, List<KeyValuePair<ulong, ulong>>>> Store =
            new Dictionary<uint, Dictionary<ulong, List<KeyValuePair<ulong, ulong>>>>();

        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 52) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            try
            {
                switch (call)
                {
                    case 2: SetTagsForEntityID(data, packet); break;
                    default: EmptyOk(packet, call); break;
                }
            }
            catch (Exception e)
            {
                Log.Error("DWTags: " + e);
                EmptyOk(packet, call);
            }
        }

        private static void SetTagsForEntityID(MessageData data, DWMessage packet)
        {
            var collection = packet.ByteBuffer.ReadUInt32();
            var entity = packet.ByteBuffer.ReadUInt64();
            var tags = new List<KeyValuePair<ulong, ulong>>();
            // Best-effort: consume remaining u64 pairs (array framing may vary)
            while (packet.ByteBuffer.RemainingBytes >= 18)
            {
                try
                {
                    var a = packet.ByteBuffer.ReadUInt64();
                    var b = packet.ByteBuffer.ReadUInt64();
                    tags.Add(new KeyValuePair<ulong, ulong>(a, b));
                }
                catch { break; }
            }
            lock (Gate)
            {
                if (!Store.ContainsKey(collection))
                    Store[collection] = new Dictionary<ulong, List<KeyValuePair<ulong, ulong>>>();
                Store[collection][entity] = tags;
            }
            Log.Info(string.Format("bdTags set collection={0} entity={1:X} tags={2}", collection, entity, tags.Count));
            EmptyOk(packet, 2);
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