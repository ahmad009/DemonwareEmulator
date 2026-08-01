using System;
using System.Collections.Generic;
using System.Linq;

namespace DWServer
{
    class GroupUser
    {
        public long userID;
        public int groupID;
    }

    class GroupUserCache
    {
        public int groupID;
        public int totalCount;
    }

    public class DWGroups
    {
        private static readonly object _lock = new object();
        private static readonly List<GroupUser> _users = new List<GroupUser>();
        private static readonly Dictionary<int, int> _counts = new Dictionary<int, int>();

        public static void Net_TcpDisconnected(MessageData data)
        {
            ulong userID = DWRouter.GetIDForData(data);
            if (userID == 0)
                return;
            RemoveUserGroups(userID);
        }

        private static void RemoveUserGroups(ulong userID)
        {
            lock (_lock)
            {
                _users.RemoveAll(u => u.userID == (long)userID);
                RebuildCounts();
            }
        }

        private static void RebuildCounts()
        {
            _counts.Clear();
            foreach (var g in _users.GroupBy(u => u.groupID))
            {
                _counts[g.Key] = g.Count();
            }
        }

        public static void DW_PacketReceived(MessageData data)
        {
            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            switch (call)
            {
                case 1:
                    SetGroups(data, packet);
                    break;
                case 4:
                    GetGroupCounts(data, packet);
                    break;
            }
        }

        private static void GetGroupCounts(MessageData data, DWMessage packet)
        {
            packet.ByteBuffer.DataTypePackingEnabled = true;
            packet.ByteBuffer.ReadByte();
            packet.ByteBuffer.ReadUInt32();
            var count = packet.ByteBuffer.ReadUInt32();

            var groupIds = new List<int>();
            for (int i = 0; i < count; i++)
            {
                groupIds.Add((int)packet.ByteBuffer.ReadUInt32());
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)28);

            lock (_lock)
            {
                var matched = groupIds.Where(id => _counts.ContainsKey(id)).ToList();
                reply.ByteBuffer.Write((uint)matched.Count);
                reply.ByteBuffer.Write((uint)matched.Count);
                foreach (var id in matched)
                {
                    reply.ByteBuffer.Write((uint)id);
                    reply.ByteBuffer.Write((uint)_counts[id]);
                }
            }

            reply.Send(true);
        }

        private static void SetGroups(MessageData data, DWMessage packet)
        {
            ulong userID = DWRouter.GetIDForData(data);
            RemoveUserGroups(userID);

            packet.ByteBuffer.DataTypePackingEnabled = true;
            packet.ByteBuffer.ReadByte();
            packet.ByteBuffer.ReadUInt32();
            var count = packet.ByteBuffer.ReadUInt32();

            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _users.Add(new GroupUser()
                    {
                        groupID = (int)packet.ByteBuffer.ReadUInt32(),
                        userID = (long)userID
                    });
                }
                RebuildCounts();
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)28);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
        }

        public static void updateCache()
        {
            lock (_lock)
            {
                RebuildCounts();
                int serverCount = 0;
                lock (DWMatch.Sessions)
                {
                    serverCount = DWMatch.Sessions.Count(session =>
                        session.Value.TitleID == TitleID.T5 &&
                        (((MatchMakingInfoT5)session.Value).LicenseType == 2 ||
                         ((MatchMakingInfoT5)session.Value).LicenseType == 4));
                }
                _counts[490] = serverCount;
                _counts[491] = 0;
                _counts[492] = 0;
            }
        }
    }
}

