using System;
using System.Collections.Generic;

namespace DWServer
{
    /// <summary>bdMail service 0x1D (29) — Demonware\reference\src\bd\bdMail.cpp</summary>
    public static class DWMail
    {
        private static readonly object Gate = new object();
        private static readonly List<MailItem> Inbox = new List<MailItem>();

        private class MailItem
        {
            public ulong Sender;
            public string Context;
            public byte[] Blob;
            public ushort Category;
            public List<ulong> Recipients = new List<ulong>();
        }

        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 29) return;
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();
            try
            {
                // task 6 = send without sender, task 7 = send with sender
                if (call == 6 || call == 7)
                    SendMail(data, packet, call);
                else
                    EmptyOk(packet, call);
            }
            catch (Exception e)
            {
                Log.Error("DWMail: " + e);
                EmptyOk(packet, call);
            }
        }

        private static void SendMail(MessageData data, DWMessage packet, byte call)
        {
            var item = new MailItem();
            if (call == 7)
            {
                try { item.Sender = packet.ByteBuffer.ReadUInt64(); } catch { }
            }
            else
            {
                item.Sender = DWRouter.GetIDForData(data);
            }
            try { item.Context = packet.ByteBuffer.ReadString() ?? ""; } catch { item.Context = ""; }
            try { item.Blob = packet.ByteBuffer.ReadBlob() ?? new byte[0]; } catch { item.Blob = new byte[0]; }
            try { item.Category = packet.ByteBuffer.ReadUInt16(); } catch { }
            uint n = 0;
            try { n = packet.ByteBuffer.ReadUInt32(); } catch { }
            for (uint i = 0; i < n; i++)
            {
                try { item.Recipients.Add(packet.ByteBuffer.ReadUInt64()); }
                catch { break; }
            }
            lock (Gate) { Inbox.Add(item); }
            Log.Info(string.Format("bdMail send cat={0} recipients={1} bytes={2}", item.Category, item.Recipients.Count, item.Blob.Length));
            EmptyOk(packet, call);
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