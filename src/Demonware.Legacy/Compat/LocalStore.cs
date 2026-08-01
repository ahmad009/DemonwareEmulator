using Demonware.Core.Store;

namespace DWServer
{
    public static class LocalStore
    {
        public static FileStore Backend { get; set; } = new FileStore();

        public static void SaveFile(string key, int userId, byte[] data) => Backend.SaveFile(key, userId, data);
        public static byte[] GetFile(string key) => Backend.GetFile(key);
        public static bool DeleteFile(string key) => Backend.DeleteFile(key);
        public static void SaveProfile(int userId, int profileInt, byte[] blob) => Backend.SaveProfile(userId, profileInt, blob);
        public static bool TryGetProfile(int userId, out int profileInt, out byte[] blob) => Backend.TryGetProfile(userId, out profileInt, out blob);
        public static void SaveServerKey(long keyHash, string key, int unkInt) => Backend.SaveServerKey(keyHash, key, unkInt);
        public static bool TryGetServerKey(long keyHash, out string key, out int unkInt) => Backend.TryGetServerKey(keyHash, out key, out unkInt);
        public static void AppendEvent(int type, byte[] data) => Backend.AppendEvent(type, data);
    }
}
