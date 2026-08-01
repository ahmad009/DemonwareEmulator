using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DWServer
{
    /// <summary>
    /// Extra bdStorage handlers that were present in helpDW C++ but missing/incomplete in DWServer.
    /// Wired from DWStorage for subtypes 5,6,8,10. IW6 tasks 11/12/13 are in DWStorage.
    /// </summary>
    public static class DWStorageEx
    {
        public static bool TryHandle(MessageData data, DWMessage packet, byte call)
        {
            switch (call)
            {
                case 5:
                    ListLegacyUserFiles(data, packet);
                    return true;
                case 6:
                    ListPublisherFiles(data, packet);
                    return true;
                case 8:
                    UpdateLegacyUserFile(data, packet);
                    return true;
                case 10:
                    SetUserFile(data, packet);
                    return true;
                // IW6 reference: tasks 11/12/13 live in DWStorage (bdStorage.cpp)
                default:
                    return false;
            }
        }

        private static void ListLegacyUserFiles(MessageData data, DWMessage packet)
        {
            // Return empty list â€” enough for clients that only need the call to succeed.
            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)5);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
            data.Arguments["handled"] = true;
        }

        private static void ListPublisherFiles(MessageData data, DWMessage packet)
        {
            try
            {
                // Optional filter string
                string filter = "";
                try { filter = packet.ByteBuffer.ReadString() ?? ""; } catch { }

                var dir = Path.Combine("data", "pub");
                string[] files = Directory.Exists(dir)
                    ? Directory.GetFiles(dir)
                    : new string[0];

                if (!string.IsNullOrEmpty(filter))
                {
                    try
                    {
                        var rx = new Regex(filter.Replace("*", ".*"), RegexOptions.IgnoreCase);
                        files = files.Where(f => rx.IsMatch(Path.GetFileName(f))).ToArray();
                    }
                    catch
                    {
                        files = files.Where(f => Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    }
                }

                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((byte)6);
                reply.ByteBuffer.Write((uint)files.Length);
                reply.ByteBuffer.Write((uint)files.Length);

                foreach (var path in files)
                {
                    var name = Path.GetFileName(path);
                    var len = new FileInfo(path).Length;
                    reply.ByteBuffer.Write(name);
                    reply.ByteBuffer.Write((uint)len);
                    reply.ByteBuffer.Write((ulong)0); // file id
                }

                reply.Send(true);
                data.Arguments["handled"] = true;
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((byte)6);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((uint)0);
                reply.Send(true);
            }
        }

        private static void UpdateLegacyUserFile(MessageData data, DWMessage packet)
        {
            // Treat as upload
            DWStorage.UploadFilePublic(data, packet);
        }

        private static void SetUserFile(MessageData data, DWMessage packet)
        {
            DWStorage.UploadFilePublic(data, packet);
        }

        private static void DeleteUserFile(MessageData data, DWMessage packet)
        {
            try
            {
                var filename = packet.ByteBuffer.ReadString();
                var user = packet.ByteBuffer.ReadUInt64();
                if (user == 0)
                {
                    user = DWRouter.GetIDForData(data);
                }
                var path = filename + "_" + user.ToString("x16");
                DWStorage.DeleteFileKey(path);

                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((byte)11);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((uint)0);
                reply.Send(true);
                data.Arguments["handled"] = true;
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
        }

        private static void GetUserFile(MessageData data, DWMessage packet)
        {
            // Same shape as GetFile
            DWStorage.GetFilePublic(data, packet);
        }
    }
}

