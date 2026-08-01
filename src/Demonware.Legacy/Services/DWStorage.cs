using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DWServer
{
    public class DWStorage
    {
        public static void DW_PacketReceived(MessageData data)
        {
            if (data.Get<int>("type") != 10) return;

            try
            {
                var packet = DWRouter.GetMessage(data);
                var call = packet.ByteBuffer.ReadByte();

                switch (call)
                {
                    case 1:
                        GetFile(data, packet);
                        break;
                    case 2:
                        SrcUploadFile(data, packet);
                        break;
                    case 3:
                        RemoveFile(data, packet);
                        break;
                    case 4:
                        SrcListFiles(data, packet);
                        break;
                    case 5:
                    case 6:
                    case 8:
                    case 10:
                        if (!DWStorageEx.TryHandle(data, packet, call))
                        {
                            Log.Debug("unhandled extended storage call " + call);
                            DWRouter.Unknown(data, packet);
                        }
                        break;
                    case 7:
                        GetPublisherFile(data, packet);
                        break;
                    case 11:
                        RemoveFile(data, packet);
                        break;
                    case 12:
                        GetFile(data, packet);
                        break;
                    case 13:
                        ListFilesByOwner(data, packet);
                        break;
                    default:
                        Log.Debug("unknown packet " + call + " in bdStorage");
                        DWRouter.Unknown(data, packet);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
        }

        public static void UploadFilePublic(MessageData mdata, DWMessage packet)
        {
            UploadFile(mdata, packet);
        }

        public static void GetFilePublic(MessageData mdata, DWMessage packet)
        {
            GetFile(mdata, packet);
        }

        public static void DeleteFileKey(string path)
        {
            LocalStore.DeleteFile(path);
        }

        private static void UploadFile(MessageData mdata, DWMessage packet)
        {
            var filename = packet.ByteBuffer.ReadString();
            var test = packet.ByteBuffer.ReadBool();
            var data = packet.ByteBuffer.ReadBlob();
            var user = (ulong)0;
            try
            {
                user = packet.ByteBuffer.ReadUInt64();
            }
            catch { }

            if (user == 0)
            {
                user = DWRouter.GetIDForData(mdata);
            }

            var path = filename + "_" + user.ToString("x16");
            Log.Debug("Trying to write " + path + "...");

            LocalStore.SaveFile(path, (int)user, data);

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)1);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
            mdata.Arguments["handled"] = true;
        }

        private static void GetFile(MessageData mdata, DWMessage packet)
        {
            // IW6 bdStorage::getFile - context, fileName, ownerUID
            string context = "";
            try { context = packet.ByteBuffer.ReadString() ?? ""; } catch { }
            var filename = packet.ByteBuffer.ReadString();
            ulong user = 0;
            try { user = packet.ByteBuffer.ReadUInt64(); } catch { }

            if (user == 0)
            {
                user = DWRouter.GetIDForData(mdata);
            }

            var path = filename + "_" + user.ToString("x16");
            Log.Debug(string.Format("bdStorage getFile ctx={0} path={1}", context, path));

            var data = LocalStore.GetFile(path) ?? LocalStore.GetFile(filename);

            if (data != null)
            {
                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((byte)12);
                reply.ByteBuffer.Write((uint)1);
                reply.ByteBuffer.Write((uint)1);
                reply.ByteBuffer.Write(filename ?? "");
                reply.ByteBuffer.Write(user);
                reply.ByteBuffer.WriteBlob(data);
                reply.Send(true);
            }
            else
            {
                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0x3E8);
                reply.Send(true);
            }
            mdata.Arguments["handled"] = true;
        }

        private static void RemoveFile(MessageData mdata, DWMessage packet)
        {
            // IW6 bdStorage::removeFile - context, fileName, ownerUID
            try { packet.ByteBuffer.ReadString(); } catch { }
            var filename = packet.ByteBuffer.ReadString();
            ulong user = 0;
            try { user = packet.ByteBuffer.ReadUInt64(); } catch { }
            if (user == 0) user = DWRouter.GetIDForData(mdata);
            LocalStore.DeleteFile(filename + "_" + user.ToString("x16"));

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)11);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
            mdata.Arguments["handled"] = true;
        }

        private static void ListFilesByOwner(MessageData mdata, DWMessage packet)
        {
            // IW6: context, owner, startDate, maxResults(u16), offset(u16), optional filter
            string context = "";
            try { context = packet.ByteBuffer.ReadString() ?? ""; } catch { }
            ulong owner = 0;
            try { owner = packet.ByteBuffer.ReadUInt64(); } catch { }
            uint startDate = 0;
            try { startDate = packet.ByteBuffer.ReadUInt32(); } catch { }
            ushort maxResults = 50;
            ushort offset = 0;
            try { maxResults = packet.ByteBuffer.ReadUInt16(); } catch { }
            try { offset = packet.ByteBuffer.ReadUInt16(); } catch { }
            string filter = null;
            try { if (packet.ByteBuffer.RemainingBytes > 2) filter = packet.ByteBuffer.ReadString(); } catch { }
            if (maxResults == 0) maxResults = 50;

            var filesDir = Path.Combine(LocalStore.Backend.Root, "files");
            var names = new List<string>();
            if (Directory.Exists(filesDir))
            {
                var all = Directory.GetFiles(filesDir, "*" + owner.ToString("x16") + ".bin");
                foreach (var f in all.Skip(offset).Take(maxResults))
                {
                    var bn = Path.GetFileNameWithoutExtension(f);
                    var idx = bn.LastIndexOf('_');
                    var name = idx > 0 ? bn.Substring(0, idx) : bn;
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    names.Add(name);
                }
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)13);
            reply.ByteBuffer.Write((uint)names.Count);
            reply.ByteBuffer.Write((uint)names.Count);
            foreach (var n in names)
            {
                reply.ByteBuffer.Write(n);
                reply.ByteBuffer.Write((ulong)0);
                reply.ByteBuffer.Write(owner);
                reply.ByteBuffer.Write(startDate);
            }
            reply.Send(true);
            mdata.Arguments["handled"] = true;
            Log.Info(string.Format("bdStorage listByOwner ctx={0} owner={1:X} count={2}", context, owner, names.Count));
        }

        private static void SrcUploadFile(MessageData mdata, DWMessage packet)
        {
            // Demonware\src upload_file: context, fileName, blob
            string context = "";
            try { context = packet.ByteBuffer.ReadString() ?? ""; } catch { }
            var filename = packet.ByteBuffer.ReadString();
            var blob = packet.ByteBuffer.ReadBlob();
            var user = DWRouter.GetIDForData(mdata);
            if (user == 0) user = 1;
            var pathKey = filename + "_" + user.ToString("x16");
            LocalStore.SaveFile(pathKey, (int)user, blob ?? new byte[0]);
            Log.Info(string.Format("bdStorage src upload ctx={0} file={1} bytes={2}", context, filename, blob == null ? 0 : blob.Length));

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)2);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((uint)0);
            reply.Send(true);
            mdata.Arguments["handled"] = true;
        }

        private static void SrcListFiles(MessageData mdata, DWMessage packet)
        {
            // Demonware\src list_files_by_owner: context, owner, maxResults
            string context = "";
            try { context = packet.ByteBuffer.ReadString() ?? ""; } catch { }
            ulong owner = 0;
            try { owner = packet.ByteBuffer.ReadUInt64(); } catch { }
            ushort maxResults = 50;
            try
            {
                if (packet.ByteBuffer.RemainingBytes > 0 && packet.ByteBuffer.PeekByte() == 6)
                    maxResults = packet.ByteBuffer.ReadUInt16();
                else
                    maxResults = (ushort)packet.ByteBuffer.ReadUInt32();
            }
            catch { }
            if (maxResults == 0) maxResults = 50;

            var filesDir = Path.Combine(LocalStore.Backend.Root, "files");
            var names = new List<string>();
            if (Directory.Exists(filesDir))
            {
                foreach (var f in Directory.GetFiles(filesDir, "*" + owner.ToString("x16") + ".bin"))
                {
                    var bn = Path.GetFileNameWithoutExtension(f);
                    var idx = bn.LastIndexOf('_');
                    names.Add(idx > 0 ? bn.Substring(0, idx) : bn);
                    if (names.Count >= maxResults) break;
                }
            }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)4);
            reply.ByteBuffer.Write((uint)names.Count);
            reply.ByteBuffer.Write((uint)names.Count);
            foreach (var n in names)
            {
                reply.ByteBuffer.Write(n);
                reply.ByteBuffer.Write((ulong)0);
                reply.ByteBuffer.Write(owner);
                reply.ByteBuffer.Write((uint)0);
            }
            reply.Send(true);
            mdata.Arguments["handled"] = true;
            Log.Info(string.Format("bdStorage src list ctx={0} owner={1:X} count={2}", context, owner, names.Count));
        }
        private static Dictionary<string, byte[]> _defaultFiles = new Dictionary<string, byte[]>();

        private static void GetPublisherFile(MessageData mdata, DWMessage packet)
        {
            try
            {
                var filename = packet.ByteBuffer.ReadString();

                Log.Debug("Trying to send " + filename + "...");

                if (!_defaultFiles.ContainsKey(filename))
                {
                    FileStream fileStream = new FileStream(@"data/pub/" + filename, FileMode.Open, FileAccess.Read);

                    int offset = 0;
                    byte[] data = new byte[fileStream.Length];
                    int remaining = (int)fileStream.Length;
                    while (remaining > 0)
                    {
                        int read = fileStream.Read(data, offset, remaining);
                        if (read <= 0)
                            throw new EndOfStreamException(
                                String.Format("End of stream reached with {0} bytes left to read", remaining));
                        remaining -= read;
                        offset += read;
                    }

                    fileStream.Close();
                    fileStream.Dispose();

                    _defaultFiles.Add(filename, data);
                }

                var reply = packet.MakeReply(1, false);
                reply.ByteBuffer.Write(0x8000000000000001);
                reply.ByteBuffer.Write((uint)0);
                reply.ByteBuffer.Write((byte)7);
                reply.ByteBuffer.Write((uint)1);
                reply.ByteBuffer.Write((uint)1);
                reply.ByteBuffer.WriteBlob(_defaultFiles[filename]);
                reply.Send(true);
            }
            catch { }
        }

        public static void FlushPublisherFiles()
        {
            lock (_defaultFiles)
            {
                _defaultFiles.Clear();
            }
        }
    }
}
