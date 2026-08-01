using System;

namespace DWServer
{
    /// <summary>
    /// bdContentStreaming service 0x32 (50) — Demonware\reference\src\bd\bdContentStreaming.cpp
    /// Tasks: 6 postUploadFile, 0x10 postCopy, 0x12 postUploadSummary.
    /// </summary>
    public static class DWContentStreaming
    {
        private static long _nextFileId = 1;

        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 50) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            try
            {
                switch (call)
                {
                    case 6:
                    case 0x10:
                        ReplyWithFileId(data, packet, call);
                        break;
                    case 0x12:
                        EmptyOk(packet, call);
                        data.Arguments["handled"] = true;
                        break;
                    default:
                        EmptyOk(packet, call);
                        data.Arguments["handled"] = true;
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error("DWContentStreaming: " + e);
                EmptyOk(packet, call);
            }
        }

        private static void ReplyWithFileId(MessageData data, DWMessage packet, byte call)
        {
            // Consume payload best-effort; client mainly needs file id result.
            try
            {
                while (packet.ByteBuffer.RemainingBytes > 0)
                {
                    // stop if we cannot read further types cleanly
                    var peek = packet.ByteBuffer.PeekByte();
                    if (peek == 16) packet.ByteBuffer.ReadString();
                    else if (peek == 6) packet.ByteBuffer.ReadUInt16();
                    else if (peek == 8) packet.ByteBuffer.ReadUInt32();
                    else if (peek == 10) packet.ByteBuffer.ReadUInt64();
                    else if (peek == 0x13) packet.ByteBuffer.ReadBlob();
                    else if (peek == 1) packet.ByteBuffer.ReadBool();
                    else break;
                }
            }
            catch { }

            var id = System.Threading.Interlocked.Increment(ref _nextFileId);
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write(call);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((ulong)id);
            reply.Send(true);
            data.Arguments["handled"] = true;
            Log.Info(string.Format("bdContentStreaming task={0} fileId={1}", call, id));
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