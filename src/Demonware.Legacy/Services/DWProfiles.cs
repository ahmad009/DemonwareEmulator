using System;
using System.Collections.Generic;

namespace DWServer
{
    public class DWProfiles
    {
        public static void DW_PacketReceived(MessageData data)
        {
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            switch (call)
            {
                case 1:
                    GetPublicInfos(data, packet);
                    break;
                case 3:
                    SetPublicInfo(data, packet);
                    break;
            }
        }

        public class PublicProfile
        {
            public int user_id;
            public int profile_int;
            public byte[] profile_blob;
            public int blobsize;
        }

        private static void GetPublicInfos(MessageData data, DWMessage packet)
        {
            var entityIDs = new List<int>();
            while (packet.ByteBuffer.PeekByte() == 10)
            {
                entityIDs.Add((int)(packet.ByteBuffer.ReadUInt64() & 0xFFFFFFFF));
            }

            var profileInfos = new List<PublicProfileInfo>();

            foreach (var id in entityIDs)
            {
                int profileInt;
                byte[] blob;
                if (LocalStore.TryGetProfile(id, out profileInt, out blob))
                {
                    profileInfos.Add(new PublicProfileInfo()
                    {
                        UserID = (ulong)(0x110000100000000 | (uint)id),
                        UnknownInt = profileInt,
                        ProfileData = blob
                    });
                }
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)8);
            reply.ByteBuffer.Write((uint)profileInfos.Count);
            reply.ByteBuffer.Write((uint)profileInfos.Count);

            foreach (var info in profileInfos)
            {
                info.Serialize(reply);
            }

            reply.Send(true);
        }

        private static void SetPublicInfo(MessageData data, DWMessage packet)
        {
            var profileInfo = new PublicProfileInfo();
            profileInfo.Deserialize(packet);

            ulong user = DWRouter.GetIDForData(data);
            var userId = (int)(user & 0xFFFFFFFF);

            LocalStore.SaveProfile(userId, profileInfo.UnknownInt, profileInfo.ProfileData ?? new byte[0]);

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)8);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
        }

        public class PublicProfileInfo
        {
            public ulong UserID { get; set; }
            public int UnknownInt { get; set; }
            public byte[] ProfileData { get; set; }

            public void Deserialize(DWMessage packet)
            {
                if (Program.Game == TitleID.T5)
                {
                    UnknownInt = packet.ByteBuffer.ReadInt32();
                }

                ProfileData = packet.ByteBuffer.ReadBlob();
            }

            public void Serialize(DWMessage packet)
            {
                packet.ByteBuffer.Write(UserID);

                if (Program.Game == TitleID.T5)
                {
                    packet.ByteBuffer.Write(UnknownInt);
                }

                packet.ByteBuffer.WriteBlob(ProfileData);
            }
        }
    }
}
