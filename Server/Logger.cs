using System;

namespace MultiplayerSFS
{
    public static class Logger
    {
        static string Date => DateTime.Now.ToString();

        public static void Debug(object obj)
        {
#if NET48
            UnityEngine.Debug.Log($"[{Date}] [DEBUG]: {obj}");
#else
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("[{0}] [DEBUG]: {1}", Date, obj);
            Console.ResetColor();
#endif
        }

        public static void Info(string msg, bool important = false)
        {
#if NET48
            UnityEngine.Debug.Log($"[{Date}] [INFO]: {msg}");
#else
            if (important)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[{0}] [INFO]: {1}", Date, msg);
            Console.ResetColor();
#endif
        }

        public static void Warning(string msg)
        {
#if NET48
            UnityEngine.Debug.LogWarning($"[{Date}] [WARN]: {msg}");
#else
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[{0}] [WARN]: {1}", Date, msg);
            Console.ResetColor();
#endif
        }

        public static void Error(string message)
        {
#if NET48
            UnityEngine.Debug.LogError($"[{Date}] [ERROR]: {message}");
#else
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[{0}] [ERROR]: {1}", Date, message);
            Console.ResetColor();
#endif
        }

        public static void Error(Exception exception)
        {
#if NET48
            UnityEngine.Debug.LogError($"[{Date}] [ERROR]: {exception}");
#else
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[{0}] [ERROR]: {1}", Date, exception);
            Console.ResetColor();
#endif
        }
    }
}
