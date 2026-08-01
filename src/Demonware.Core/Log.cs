namespace Demonware.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Banner()
    {
        lock (Gate)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("  +--------------------------------------------------+");
            Console.WriteLine("  |           Demonware Emulator                     |");
            Console.WriteLine("  |  STUN 3074u | Modern 3074t | Legacy 3078t | :80  |");
            Console.WriteLine("  |  legacy: t5/t6/iw5/iw6   modern: iw7/t7/t8/s1/s2 |");
            Console.WriteLine("  +--------------------------------------------------+");
            Console.ForegroundColor = old;
            Console.WriteLine();
        }
    }

    public static void Info(string channel, string message) => Write(ConsoleColor.Gray, "INF", channel, message);
    public static void Ok(string channel, string message) => Write(ConsoleColor.Green, "OK ", channel, message);
    public static void Warn(string channel, string message) => Write(ConsoleColor.Yellow, "WRN", channel, message);
    public static void Error(string channel, string message) => Write(ConsoleColor.Red, "ERR", channel, message);
    public static void Debug(string channel, string message) => Write(ConsoleColor.DarkGray, "DBG", channel, message);

    private static void Write(ConsoleColor color, string level, string channel, string message)
    {
        lock (Gate)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
            Console.ForegroundColor = color;
            Console.Write($" | {level} | ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{channel,-12}");
            Console.ForegroundColor = old;
            Console.WriteLine($" | {message}");
        }
    }
}
