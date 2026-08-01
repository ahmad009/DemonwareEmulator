using System;

namespace DWServer
{
    /// <summary>
    /// bdBandwidthTest (service type 18) — IW6 payload from helpDW.
    /// </summary>
    public static class DWBandwidth
    {
        // Same bytes as helpDW demonware/services/bdBandwidthTest.cpp (bandwidth_iw6)
        private static readonly byte[] BandwidthIw6 =
        {
            0x0F, 0xC1, 0x1C, 0x37, 0xB8, 0xEF, 0x7C, 0xD6, 0x00, 0x00, 0x04,
            0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0xF4, 0x01, 0x00, 0x00, 0xD0, 0x07,
            0x00, 0x00, 0x10, 0x27, 0x00, 0x00, 0x88, 0x13, 0x00, 0x00, 0xF4, 0x01,
            0x00, 0x00, 0x02, 0x0C, 0x88, 0xB3, 0x04, 0x65, 0x89, 0xBF, 0xC3, 0x6A,
            0x27, 0x94, 0xD4, 0x8F
        };

        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            if (type != 18)
            {
                return;
            }

            try
            {
                var packet = DWRouter.GetMessage(data);
                // helpDW replies with create_message(5) + encrypted bandwidth blob
                var reply = packet.MakeReply(5, false);
                reply.ByteBuffer.Write(BandwidthIw6);
                reply.Send(true);
                data.Arguments["handled"] = true;
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
        }
    }
}
