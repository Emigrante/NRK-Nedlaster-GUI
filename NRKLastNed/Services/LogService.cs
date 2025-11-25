using System;
using System.IO;
using NRKLastNed.Models;

namespace NRKLastNed.Services
{
    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Info = 2,
        Debug = 3
    }

    public static class LogService
    {
        private static string _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public static void Log(string message, LogLevel level, AppSettings settings)
        {
            if (!settings.EnableLogging || level > settings.LogLevel) return;

            try
            {
                if (!Directory.Exists(_logFolder)) Directory.CreateDirectory(_logFolder);

                string fileName = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
                string filePath = Path.Combine(_logFolder, fileName);
                string logLine = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

                File.AppendAllText(filePath, logLine + Environment.NewLine);
            }
            catch
            {
                // Hvis logging feiler, ignorerer vi det for å ikke krasje appen
            }
        }
    }
}