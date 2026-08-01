using System;

namespace DWServer
{
    /// <summary>bdEventLog service 0x43 (67) — reference: task 1 recordEvent, task 2 recordEventBin</summary>
    public class DWEventLog
    {
        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 67) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            switch (call)
            {
                case 1:
                    RecordEvent(data, packet);
                    break;
                case 2:
                    LogBinaryEvent(data, packet);
                    break;
                default:
                    EmptyOk(packet, call);
                    break;
            }
        }

        private static void RecordEvent(MessageData mdata, DWMessage packet)
        {
            string ev = "";
            try { ev = packet.ByteBuffer.ReadString() ?? ""; } catch { }
            uint category = 0;
            try { category = packet.ByteBuffer.ReadUInt32(); } catch { }
            LocalStore.AppendEvent((int)category, System.Text.Encoding.UTF8.GetBytes(ev));
            Log.Debug("Logged event cat=" + category + " len=" + ev.Length);

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)1);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((ulong)DateTime.UtcNow.Ticks);
            reply.Send(true);
            mdata.Arguments["handled"] = true;
        }

        private static void LogBinaryEvent(MessageData mdata, DWMessage packet)
        {
            var blob = packet.ByteBuffer.ReadBlob();
            var type = packet.ByteBuffer.ReadUInt32();
            LocalStore.AppendEvent((int)type, blob);
            Log.Debug("Logged binary event type " + type);
            EmptyOk(packet, 2);
            mdata.Arguments["handled"] = true;
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