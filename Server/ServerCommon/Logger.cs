using System;

namespace MultiplayerSFS.ServerCommon
{
    public static class Logger
    {
        public static void Info(string message, bool verbose = false)
        {
            Console.WriteLine($"[INFO] {message}");
        }

        public static void Warning(string message)
        {
            Console.WriteLine($"[WARNING] {message}");
        }

        public static void Error(string message)
        {
            Console.WriteLine($"[ERROR] {message}");
        }

        public static void Error(Exception exception)
        {
            Console.WriteLine($"[ERROR] {exception.Message}");
            Console.WriteLine($"[ERROR] Stack trace: {exception.StackTrace}");
        }

        public static void Debug(string message)
        {
            Console.WriteLine($"[DEBUG] {message}");
        }
    }
}
