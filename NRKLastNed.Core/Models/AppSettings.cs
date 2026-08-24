using System;
using System.IO;
using System.Text.Json;
using NRKLastNed.Core.Services;

namespace NRKLastNed.Core.Models
{
    public class AppSettings
    {
        // TV og Radio nedlastingsmapper
        public string TvOutputFolder { get; set; } = "";
        public string RadioOutputFolder { get; set; } = "";

        // Alternativ: Bruk samme mappe for bade TV og Radio
        public bool UseSameFolderForBoth { get; set; } = false;

        // Legacy - for bakoverkompatibilitet
        public string OutputFolder 
        { 
            get => TvOutputFolder;
            set => TvOutputFolder = value;
        }

        public string TempFolder { get; set; } = "";
        public bool UseSystemTemp { get; set; } = true;

        // Standard opplosning (720 som standard)
        public string DefaultResolution { get; set; } = "720";

        // Tema: "System", "Light", "Dark"
        public string AppTheme { get; set; } = "Dark";

        // Logging
        public bool EnableLogging { get; set; } = true;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        private static readonly string _settingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NRKLastNed");

        private static readonly string _settingsPath = Path.Combine(_settingsFolder, "settings.json");

        public AppSettings()
        {
            string defaultBase = PlatformService.Instance.GetDefaultOutputFolder();
            TvOutputFolder = Path.Combine(defaultBase, "TV");
            RadioOutputFolder = Path.Combine(defaultBase, "Radio");
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                {
                    Directory.CreateDirectory(_settingsFolder);
                }

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                LogService.Log($"Feil ved lagring av innstillinger: {ex.Message}", LogLevel.Error, settings);
            }
        }

        public static AppSettings Load()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    if (string.IsNullOrEmpty(settings.TvOutputFolder))
                    {
                        string defaultBase = PlatformService.Instance.GetDefaultOutputFolder();
                        settings.TvOutputFolder = Path.Combine(defaultBase, "TV");
                    }
                    if (string.IsNullOrEmpty(settings.RadioOutputFolder))
                    {
                        string defaultBase = PlatformService.Instance.GetDefaultOutputFolder();
                        settings.RadioOutputFolder = Path.Combine(defaultBase, "Radio");
                    }

                    return settings;
                }
                catch (Exception ex)
                {
                    LogService.Log($"Feil ved lasting av innstillinger: {ex.Message}", LogLevel.Error);
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }
    }
}
