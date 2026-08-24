using System;
using System.IO;
using System.Text;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public static class LogService
    {
        private static readonly object _lock = new();
        private static readonly string _logFolder;
        private static string? _currentLogDate;
        private static StreamWriter? _writer;

        static LogService()
        {
            _logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NRKLastNed",
                "Logs");

            try
            {
                if (!Directory.Exists(_logFolder))
                {
                    Directory.CreateDirectory(_logFolder);
                }
            }
            catch { }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => FlushAndClose();
        }

        public static void Log(string message, LogLevel level, AppSettings? settings = null)
        {
            if (settings != null && (!settings.EnableLogging || level > settings.LogLevel))
                return;

            string nowFormatted = DateTime.Now.ToString("HH:mm:ss");
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string logLine = $"[{nowFormatted}] [{level}] {message}";

            lock (_lock)
            {
                try
                {
                    EnsureWriter(today);
                    _writer?.WriteLine(logLine);
                }
                catch
                {
                    // Ignorer feil for a ikke krasje appen
                }
            }
        }

        private static void EnsureWriter(string today)
        {
            if (_writer != null && _currentLogDate == today)
                return;

            FlushAndClose();

            try
            {
                if (!Directory.Exists(_logFolder))
                {
                    Directory.CreateDirectory(_logFolder);
                }

                string fileName = $"log_{today}.txt";
                string filePath = Path.Combine(_logFolder, fileName);

                var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };
                _currentLogDate = today;
            }
            catch
            {
                _writer = null;
            }
        }

        private static void FlushAndClose()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { }
            finally
            {
                _writer = null;
                _currentLogDate = null;
            }
        }
    }
}
