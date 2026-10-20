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
        private static int _currentDayOfYear = -1;
        private static int _currentYear = -1;
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

            DateTime now = DateTime.Now;

            lock (_lock)
            {
                try
                {
                    EnsureWriter(now);
                    if (_writer != null)
                    {
                        _writer.WriteLine($"[{now:HH:mm:ss}] [{level}] {message}");

                        // Tving kun umiddelbar disk-flush på Info/Error, slik at Debug-logging ikke bremser nedlasting
                        if (level <= LogLevel.Info)
                        {
                            _writer.Flush();
                        }
                    }
                }
                catch
                {
                    // Ignorer feil for å ikke krasje appen
                }
            }
        }

        private static void EnsureWriter(DateTime now)
        {
            if (_writer != null && _currentDayOfYear == now.DayOfYear && _currentYear == now.Year)
                return;

            FlushAndClose();

            try
            {
                if (!Directory.Exists(_logFolder))
                {
                    Directory.CreateDirectory(_logFolder);
                }

                string fileName = $"log_{now:yyyy-MM-dd}.txt";
                string filePath = Path.Combine(_logFolder, fileName);

                var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096);
                _writer = new StreamWriter(fileStream, Encoding.UTF8, bufferSize: 4096) { AutoFlush = false };
                _currentDayOfYear = now.DayOfYear;
                _currentYear = now.Year;
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
                _currentDayOfYear = -1;
                _currentYear = -1;
            }
        }
    }
}