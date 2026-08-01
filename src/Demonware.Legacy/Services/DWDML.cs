using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DWServer
{
    public class DWDML
    {
        public static LookupService _geoIP;

        static DWDML()
        {
            try
            {
                if (System.IO.File.Exists("GeoLiteCity.dat"))
                {
                    _geoIP = new LookupService("GeoLiteCity.dat", LookupService.GEOIP_STANDARD);
                }
            }
            catch (Exception e)
            {
                Log.Error("GeoIP init failed: " + e.Message);
            }
        }

        public static void DW_PacketReceived(MessageData data)
        {
            var type = data.Get<int>("type");
            var crypt = data.Get<bool>("crypt");

            var packet = DWRouter.GetMessage(data);
            var call = packet.ByteBuffer.ReadByte();

            switch (call)
            {
                case 2:
                    GetUserData(data, packet);
                    break;
                default:
                    Log.Debug("unknown packet " + call + " in bdDML");
                    break;
            }
        }

        private static void GetUserData(MessageData mdata, DWMessage packet)
        {
            var ip = mdata.Get<string>("cid").Split(':')[0];
            string countryCode = "US";
            string countryName = "United States";
            string region = "Unknown";
            string city = "Unknown";
            float lat = 0, lon = 0;

            try
            {
                if (_geoIP != null)
                {
                    var location = _geoIP.getLocation(ip);
                    if (location != null)
                    {
                        countryCode = location.countryCode ?? countryCode;
                        countryName = location.countryName ?? countryName;
                        region = location.regionName ?? region;
                        city = location.city ?? city;
                        lat = (float)location.latitude;
                        lon = (float)location.longitude;
                    }
                }
            }
            catch { }

            var reply = packet.MakeReply(1, false);
            reply.ByteBuffer.Write(0x8000000000000001);
            reply.ByteBuffer.Write((uint)0);
            reply.ByteBuffer.Write((byte)8);
            reply.ByteBuffer.Write(1);
            reply.ByteBuffer.Write(1);

            reply.ByteBuffer.Write(countryCode);
            reply.ByteBuffer.Write(countryName);
            reply.ByteBuffer.Write(region);
            reply.ByteBuffer.Write(city);
            reply.ByteBuffer.Write(lat);
            reply.ByteBuffer.Write(lon);
            // helpDW also sends asn + timezone on bdDMLRawData — keep extra fields optional
            reply.ByteBuffer.Write((uint)0x2119);
            reply.ByteBuffer.Write("+00:00");

            Log.Info(string.Format("Sending reply to GetUserData: {0} - {1} - {2}.", countryCode, countryName, region));
            reply.Send(true);
            mdata.Arguments["handled"] = true;
        }
    }
}
