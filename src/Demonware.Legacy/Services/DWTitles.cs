using System;
using System.Collections.Generic;

namespace DWServer
{
    /// <summary>bdTitleUtilities service 0x0C (12) — reference: task 6 getServerTime, task 9 getUserNames</summary>
    class DWTitles
    {
        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            var crypt = data.Get<bool>("crypt");
            if (!(type == 12 && crypt)) return;

            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            switch (call)
            {
                case 6:
                    GetServerTime(data, packet);
                    break;
                case 9:
                    GetUserNames(data, packet);
                    break;
                default:
                    Log.Info("unknown packet " + call + " in bdTitleUtilities");
                    EmptyOk(packet, call);
                    break;
            }
        }

        private static void GetServerTime(MessageData data, DWMessage packet)
        {
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000000UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)6);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write(DateTime.UtcNow.ToUnixTime());
            reply.Send(true);
            data.Arguments["handled"] = true;
        }

        private static void GetUserNames(MessageData data, DWMessage packet)
        {
            var ids = new List<ulong>();
            while (packet.ByteBuffer.RemainingBytes >= 9)
            {
                try { ids.Add(packet.ByteBuffer.ReadUInt64()); }
                catch { break; }
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)9);
            reply.ByteBuffer.Write((uint)ids.Count);
            reply.ByteBuffer.Write((uint)ids.Count);
            foreach (var id in ids)
            {
                string name;
                if (!DWRouter.CIDToName.TryGetValue(data.Get<string>("cid"), out name) || string.IsNullOrEmpty(name))
                    name = "user_" + (id & 0xFFFFFFFF).ToString("x");
                // bdUserInfo typically: userId + username string
                reply.ByteBuffer.Write(id);
                reply.ByteBuffer.Write(name);
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