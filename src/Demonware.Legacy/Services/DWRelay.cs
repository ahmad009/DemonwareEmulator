using System;
using System.Text;

namespace DWServer
{
    /// <summary>
    /// bdRelayService (service type 86) — from helpDW C++ emulator.
    /// </summary>
    public static class DWRelay
    {
        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            if (type != 86)
            {
                return;
            }

            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            try
            {
                switch (call)
                {
                    case 3:
                        GetCredentials(data, packet);
                        break;
                    case 4:
                        GetCredentialsFromTicket(data, packet);
                        break;
                    default:
                        Log.Debug("unknown packet " + call + " in bdRelayService");
                        SendEmptyOk(packet, call);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
                SendEmptyOk(packet, call);
            }
        }

        private static void GetCredentials(MessageData data, DWMessage packet)
        {
            // unk1, user_id, platform
            try
            {
                packet.ByteBuffer.ReadUInt32();
                var userId = packet.ByteBuffer.ReadUInt64();
                var platform = packet.ByteBuffer.ReadString();
                Log.Debug(string.Format("bdRelay get_credentials user={0:X16} platform={1}", userId, platform));
            }
            catch { }

            var ourId = DWRouter.GetIDForData(data);
            if (ourId == 0)
            {
                ourId = 0x110000100000001UL;
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)3);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((uint)1);

            // DebugObjectUCD-like payload (simplified, enough for clients that only check success)
            reply.ByteBuffer.Write(ourId);
            reply.ByteBuffer.Write("pc");
            reply.ByteBuffer.Write((uint)2); // user_ids count hint
            reply.ByteBuffer.Write(ourId);
            reply.ByteBuffer.Write((ulong)0x00659CD6);
            reply.ByteBuffer.Write("pc");
            reply.ByteBuffer.Write("ucd");
            reply.Send(true);
            data.Arguments["handled"] = true;
        }

        private static void GetCredentialsFromTicket(MessageData data, DWMessage packet)
        {
            try
            {
                var ticket = packet.ByteBuffer.ReadString();
                Log.Debug("bdRelay get_credentials_from_ticket len=" + (ticket ?? "").Length);
            }
            catch { }

            var ourId = DWRouter.GetIDForData(data);
            if (ourId == 0)
            {
                ourId = 0x1A586A45744DA396UL;
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)4);
            reply.ByteBuffer.Write((uint)1);
            reply.ByteBuffer.Write((uint)1);

            reply.ByteBuffer.Write((ulong)0x1A586A45744DA396);
            reply.ByteBuffer.Write(ourId);
            reply.ByteBuffer.Write(16326195462233142067UL);
            reply.ByteBuffer.Write("youtube");
            reply.ByteBuffer.Write("steam");
            reply.ByteBuffer.Write("uno");
            reply.Send(true);
            data.Arguments["handled"] = true;
        }

        private static void SendEmptyOk(DWMessage packet, byte subType)
        {
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write(subType);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
        }
    }
}
