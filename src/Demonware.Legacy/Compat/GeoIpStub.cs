namespace DWServer
{
    public class LookupService
    {
        public const int GEOIP_STANDARD = 0;
        public LookupService(string path, int mode) { }
        public Location getLocation(string ip) => null;
    }

    public class Location
    {
        public string countryCode;
        public string countryName;
        public string regionName;
        public string city;
        public double latitude;
        public double longitude;
    }
}
