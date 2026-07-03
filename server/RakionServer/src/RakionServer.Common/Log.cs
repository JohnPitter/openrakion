using System;
using System.IO;

namespace RakionServer.Common
{
    /// <summary>
    /// Logger simples com timestamp e categoria, em cores no console + arquivo.
    /// Toda jornada/feature dos servidores loga por aqui (debug e visualizacao).
    /// </summary>
    public static class Log
    {
        private static readonly object _gate = new object();
        private static StreamWriter? _file;

        /// <summary>Quando false, mensagens Debug() sao suprimidas.</summary>
        public static bool DebugEnabled { get; set; } = true;

        /// <summary>Ativa log em arquivo. Chamar uma vez no boot.</summary>
        public static void EnableFileLog(string path)
        {
            lock (_gate)
            {
                _file?.Dispose();
                _file = new StreamWriter(path, append: true) { AutoFlush = true };
            }
        }

        // Overloads de 1 argumento (mensagem ja formatada) — categoria generica "op".
        public static void Info(string message) => Write(ConsoleColor.Gray, "INFO", "op", message, EmptyArgs);
        public static void Ok(string message) => Write(ConsoleColor.Green, "OK  ", "op", message, EmptyArgs);
        public static void Warn(string message) => Write(ConsoleColor.Yellow, "WARN", "op", message, EmptyArgs);
        public static void Error(string message) => Write(ConsoleColor.Red, "ERR ", "op", message, EmptyArgs);
        public static void Debug(string message) { if (DebugEnabled) Write(ConsoleColor.DarkGray, "DBG ", "op", message, EmptyArgs); }

        private static readonly object[] EmptyArgs = System.Array.Empty<object>();

        public static void Info(string category, string format, params object[] args)
            => Write(ConsoleColor.Gray, "INFO", category, format, args);

        public static void Ok(string category, string format, params object[] args)
            => Write(ConsoleColor.Green, "OK  ", category, format, args);

        public static void Warn(string category, string format, params object[] args)
            => Write(ConsoleColor.Yellow, "WARN", category, format, args);

        public static void Error(string category, string format, params object[] args)
            => Write(ConsoleColor.Red, "ERR ", category, format, args);

        public static void Debug(string category, string format, params object[] args)
        {
            if (DebugEnabled)
                Write(ConsoleColor.DarkGray, "DBG ", category, format, args);
        }

        private static void Write(ConsoleColor color, string level, string category, string format, object[] args)
        {
            string msg = (args is { Length: > 0 }) ? string.Format(format, args) : format;
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{category}] {msg}";
            lock (_gate)
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
                try { _file?.WriteLine(line); } catch { }
            }
        }
    }
}
