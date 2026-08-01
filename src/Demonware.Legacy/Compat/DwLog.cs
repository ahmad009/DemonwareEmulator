namespace DWServer
{
    public static class Log
    {
        public static void Debug(string message) => Demonware.Core.Log.Debug("Legacy", message);
        public static void Info(string message) => Demonware.Core.Log.Info("Legacy", message);
        public static void Error(string message) => Demonware.Core.Log.Error("Legacy", message);
        public static void Verbose(string message) => Demonware.Core.Log.Debug("Legacy", message);
        public static void Warning(string message) => Demonware.Core.Log.Warn("Legacy", message);
    }
}
