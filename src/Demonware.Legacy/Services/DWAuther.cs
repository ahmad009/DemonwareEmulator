using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace DWServer
{
    class DWAuther
    {
        private static Dictionary<ulong, string> _onlineIDToName = new Dictionary<ulong, string>();

        public static string GetNameForID(ulong id)
        {
            if (!_onlineIDToName.ContainsKey(id))
            {
                return id.ToString("X16");
            }

            return _onlineIDToName[id];
        }


        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            var crypt = data.Get<bool>("crypt");

            if (!crypt)
            {
                if (type == 28)
                {
                    HandleClientAuth(data);
                }
                else if (type == 12)
                {
                    HandleServerAuth(data);
                }
                else if (type == 26)
                {
                    HandleIW5ServerAuth(data, type);
                }
            }
        }

        private class ClientAuthstate
        {
            public DWMessage Packet { get; set; }
            public byte[] Ticket { get; set; }
            public uint Random { get; set; }
            public uint GameID { get; set; }
            public bool OddBool { get; set; }
            public string Source { get; set; }
        }

        private static void HandleClientAuth(MessageData data)
        {
            var packet = DWRouter.GetMessage(data);

            bool unknownBool;
            uint randomNumber;
            uint gameID;
            uint ticketLength;
            byte[] ticket;

            packet.BitBuffer.UseDataTypes = false;
            packet.BitBuffer.ReadBoolean(out unknownBool);
            packet.BitBuffer.UseDataTypes = true;

            packet.BitBuffer.ReadUInt32(out randomNumber);
            packet.BitBuffer.ReadUInt32(out gameID);
            packet.BitBuffer.ReadUInt32(out ticketLength);

            ticket = new byte[ticketLength];
            packet.BitBuffer.ReadBytes((int)ticketLength, out ticket);

            var cid = data.Get<string>("cid");

            // and the connection
            //var ci = TCPHandler.Connections.Find(c => c.ConnectionID == cid);

            var SourceSocket = cid.Split(':');
            // create state
            var state = new ClientAuthstate()
            {
                GameID = gameID,
                Ticket = ticket,
                Packet = packet,
                Random = randomNumber,
                OddBool = unknownBool,
                Source = SourceSocket[0]
            };

            // start thread
            var thread = new Thread(DoClientAuth);
            thread.Start(state);
        }

        private static void DoClientAuth(object stateo)
        {
            var state = (ClientAuthstate)stateo;

            try
            {
                var npticket = DWTickets.ParseNPTicket(state.Ticket);
                var userID = (int)(npticket.SteamID & 0xFFFFFFFF);
                if (userID == 0) userID = 1;

                var ivBase = BitConverter.ToUInt32(DWCrypto.GenerateRandom(4), 0);
                var iv = DWCrypto.CalculateInitialVector(ivBase);
                var key = npticket.EncryptionKey;
                if (key == null || key.Length != 24)
                {
                    key = DWCrypto.GenerateRandom(24);
                }

                var globalKey = DWCrypto.GenerateRandom(24);

                var gameTicket = DWTickets.BuildGameTicket(globalKey, state.GameID, npticket.NickName ?? "Player", 0);
                var lsgTicket = DWTickets.BuildLSGTicket(globalKey, npticket.SteamID, userID, npticket.NickName ?? "Player");

                var encryptedGameTicket = DWCrypto.Encrypt(iv, key, gameTicket);

                var reply = state.Packet.MakeReply(29, true);
                reply.BitBuffer.UseDataTypes = false;
                reply.BitBuffer.WriteBoolean(false);
                reply.BitBuffer.WriteUInt32(700);
                reply.BitBuffer.WriteUInt32(ivBase);
                reply.BitBuffer.WriteBytes(encryptedGameTicket);
                reply.BitBuffer.WriteBytes(lsgTicket);
                reply.Send(false);

                Log.Debug("user " + userID + " authenticated client (local): " + state.Source);
            }
            catch (Exception e)
            {
                Log.Debug("Exception: " + e.ToString());
            }
        }

        private class ServerAuthstate
        {
            public DWMessage Packet { get; set; }
            public ulong KeyData { get; set; }
            public uint Random { get; set; }
            public uint GameID { get; set; }
            public bool OddBool { get; set; }
        }

        private static void HandleServerAuth(MessageData data)
        {
            var packet = DWRouter.GetMessage(data);

            bool unknownBool;
            uint randomNumber;
            uint gameID;
            byte[] keyDataBuf = new byte[8];

            packet.BitBuffer.UseDataTypes = false;
            packet.BitBuffer.ReadBoolean(out unknownBool);
            packet.BitBuffer.UseDataTypes = true;

            packet.BitBuffer.ReadUInt32(out randomNumber);
            packet.BitBuffer.ReadUInt32(out gameID);
            packet.BitBuffer.Read(64, keyDataBuf);

            // create state
            var state = new ServerAuthstate()
            {
                GameID = gameID,
                KeyData = BitConverter.ToUInt64(keyDataBuf, 0),
                Packet = packet,
                Random = randomNumber,
                OddBool = unknownBool
            };

            // start thread — dedicated auth for T5/IW5/IW6
            if (Program.Game == TitleID.IW5)
            {
                var thread = new Thread(DoIW5ServerAuth);
                thread.Start(state);
            }
            else
            {
                var thread = new Thread(DoServerAuth);
                thread.Start(state);
            }
        }

        private static void DoServerAuth(object stateo)
        {
            var state = (ServerAuthstate)stateo;

            try
            {
                var licenseType = 4;
                var userID = 1;
                ulong keyMaterial = state.KeyData;

                var thash = new TigerHash();
                var key = thash.ComputeHash(BitConverter.GetBytes(keyMaterial));
                if (key.Length > 24)
                {
                    Array.Resize(ref key, 24);
                }
                else if (key.Length < 24)
                {
                    var padded = new byte[24];
                    Array.Copy(key, padded, key.Length);
                    key = padded;
                }

                var ivBase = BitConverter.ToUInt32(DWCrypto.GenerateRandom(4), 0);
                var iv = DWCrypto.CalculateInitialVector(ivBase);
                var globalKey = DWCrypto.GenerateRandom(24);

                var gameTicket = DWTickets.BuildGameTicket(globalKey, state.GameID, "IW6-Server", (byte)licenseType);
                var lsgTicket = DWTickets.BuildLSGTicket(globalKey, state.KeyData, userID, "IW6-Server");

                var encryptedGameTicket = DWCrypto.Encrypt(iv, key, gameTicket);

                var reply = state.Packet.MakeReply(13, true);
                reply.BitBuffer.UseDataTypes = false;
                reply.BitBuffer.WriteBoolean(false);
                reply.BitBuffer.WriteUInt32(700);
                reply.BitBuffer.WriteUInt32(ivBase);
                reply.BitBuffer.WriteBytes(encryptedGameTicket);
                reply.BitBuffer.WriteBytes(lsgTicket);
                reply.Send(false);

                Log.Debug("user " + userID + " authenticated server (local)");
            }
            catch (Exception e)
            {
                Log.Debug("Exception: " + e.ToString());
            }
        }

        private class IW5ServerAuthstate
        {
            public DWMessage Packet { get; set; }
            public ulong KeyData { get; set; }
            public uint Random { get; set; }
            public uint GameID { get; set; }
            public bool OddBool { get; set; }
        }

        private static void HandleIW5ServerAuth(MessageData data, int type)
        {
            var packet = DWRouter.GetMessage(data);

            bool unknownBool;
            uint randomNumber;
            uint gameID;
            byte[] rsaKeyBuf = new byte[1120 / 8];

            packet.BitBuffer.UseDataTypes = false;
            packet.BitBuffer.ReadBoolean(out unknownBool);
            packet.BitBuffer.UseDataTypes = true;

            packet.BitBuffer.ReadUInt32(out randomNumber);
            packet.BitBuffer.ReadUInt32(out gameID);

            // create state
            var state = new IW5ServerAuthstate()
            {
                GameID = gameID,
                Packet = packet,
                Random = randomNumber,
                OddBool = unknownBool
            };

            if (type == 26)
            {
                packet.BitBuffer.Read(1120, rsaKeyBuf);

                // start thread
                var thread = new Thread(CreateIW5ServerKey);
                thread.Start(state);
            }
        }

        public class ServerKey
        {
            public long keyHash;
            public string key;
            public int unkInt;
        }

        private static void DoIW5ServerAuth(object stateo)
        {
            var state = (ServerAuthstate)stateo;

            try
            {
                string keyString = null;
                int unk;
                if (LocalStore.TryGetServerKey((long)state.KeyData, out keyString, out unk))
                {
                    // found on disk
                }
                else
                {
                    keyString = "OFFLINE-" + state.KeyData.ToString("X16");
                    Log.Info("IW5 server auth local fallback");
                }

                var thash = new TigerHash();
                var key = thash.ComputeHash(Encoding.ASCII.GetBytes(keyString));

                var ivBase = BitConverter.ToUInt32(DWCrypto.GenerateRandom(4), 0);
                var iv = DWCrypto.CalculateInitialVector(ivBase);
                var globalKey = DWCrypto.GenerateRandom(24);

                var gameTicket = DWTickets.BuildGameTicket(globalKey, state.GameID, "", 0);
                var lsgTicket = DWTickets.BuildLSGTicket(globalKey, state.KeyData, 1, "");

                var encryptedGameTicket = DWCrypto.Encrypt(iv, key, gameTicket);

                var reply = state.Packet.MakeReply(13, true);
                reply.BitBuffer.UseDataTypes = false;
                reply.BitBuffer.WriteBoolean(false);
                reply.BitBuffer.WriteUInt32(700);
                reply.BitBuffer.WriteUInt32(ivBase);
                reply.BitBuffer.WriteBytes(encryptedGameTicket);
                reply.BitBuffer.WriteBytes(lsgTicket);
                reply.Send(false);
            }
            catch (Exception e)
            {
                Log.Debug("Exception: " + e.ToString());
            }
        }

        private static void CreateIW5ServerKey(object stateo)
        {
            var state = (IW5ServerAuthstate)stateo;

            try
            {
                Log.Debug("got a request for a new IW5 dedi key; seems fun to me");

                var passGen = new PasswordGenerator();
                passGen.Maximum = 20;
                passGen.Minimum = 20;
                var key = passGen.Generate();

                key = string.Format("X{0}-{1}-{2}-{3}-{4}", key.Substring(1, 3), key.Substring(4, 4), key.Substring(8, 4), key.Substring(12, 4), key.Substring(16, 4));

                var thash = new TigerHash();
                var hash = thash.ComputeHash(Encoding.ASCII.GetBytes(key));
                var keyHash = BitConverter.ToInt64(hash, 0);

                var keyEntry = new ServerKey();
                keyEntry.key = key;
                keyEntry.keyHash = keyHash;
                keyEntry.unkInt = new Random().Next();
                LocalStore.SaveServerKey(keyEntry.keyHash, keyEntry.key, keyEntry.unkInt);

                var keyStuff = new byte[86];
                Array.Copy(Encoding.ASCII.GetBytes(key), keyStuff, key.Length);

                var obfuscationKey = "43FCB2ACF2D72593DD7CD1C69E0F03C07229F4C83166F7B05BA0C5FE3AA3A2D93EK2495783KDKN92939DK";
                var i = 0;

                foreach (var character in obfuscationKey)
                {
                    keyStuff[i] ^= (byte)character;
                    i++;
                }

                var ivBase = BitConverter.ToUInt32(DWCrypto.GenerateRandom(4), 0);
                var iv = DWCrypto.CalculateInitialVector(ivBase);
                var globalKey = DWCrypto.GenerateRandom(24);

                var gameTicket = DWTickets.BuildGameTicket(globalKey, state.GameID, "", (byte)0);
                var lsgTicket = DWTickets.BuildLSGTicket(globalKey, (ulong)keyHash, 1, "");

                var encryptedGameTicket = DWCrypto.Encrypt(iv, hash, gameTicket);

                var reply = state.Packet.MakeReply(25, true);
                reply.BitBuffer.UseDataTypes = false;
                reply.BitBuffer.WriteBoolean(false);
                reply.BitBuffer.WriteUInt32(700);
                reply.BitBuffer.WriteUInt32(ivBase);
                reply.BitBuffer.WriteBytes(encryptedGameTicket);
                reply.BitBuffer.WriteBytes(lsgTicket);
                reply.BitBuffer.WriteBytes(keyStuff);
                reply.BitBuffer.WriteInt32(keyEntry.unkInt);

                reply.Send(false);
            }
            catch (Exception e)
            {
                Log.Debug("Exception: " + e.ToString());
            }
        }
    }
}
