using System;
using System.IO;

namespace NetworkScannerTool
{
    internal static class AppLogger
    {
        private static readonly object Sync = new object();

        public static string LogDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NetworkScannerTool");
            }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogDirectory, "network-scanner.log"); }
        }

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                Write("INFO", "Application", "started");
            }
            catch
            {
            }
        }

        public static void Info(string operation, string message)
        {
            Write("INFO", operation, message);
        }

        public static void Warning(string operation, string message)
        {
            Write("WARN", operation, message);
        }

        public static void Error(string operation, string target, Exception exception)
        {
            Write("ERROR", operation, (target ?? "") + " | " + exception);
        }

        private static void Write(string level, string operation, string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                string line = string.Format(
                    "{0:O}\t{1}\t{2}\t{3}\r\n",
                    DateTime.UtcNow,
                    level ?? "INFO",
                    operation ?? "",
                    message ?? "");
                lock (Sync)
                {
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch
            {
                // Logging must never terminate the application.
            }
        }
    }
}
