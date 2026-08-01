using System;

namespace DWServer
{
    /// <summary>bdAntiCheat service 0x26 (38) — reference task 2 answerChallenges; also accept helpDW task 4</summary>
    public static class DWAnticheat
    {
        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 38) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            // Accept payload then empty-ok — DS just needs success.
            try { while (packet.ByteBuffer.RemainingBytes > 0) { packet.ByteBuffer.Read(1); } } catch { }
            SendEmptyOk(packet, call);
            data.Arguments["handled"] = true;
            Log.Debug("bdAntiCheat task=" + call + " ok");
        }

        private static void SendEmptyOk(DWMessage packet, byte subType)
        {
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001UL);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write(subType);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
        }
    }
}