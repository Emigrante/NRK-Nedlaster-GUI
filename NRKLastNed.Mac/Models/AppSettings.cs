using System;
using System.IO;
using System.Text.Json;
using NRKLastNed.Mac.Services;

namespace NRKLastNed.Mac.Models
{
    public class AppSettings
    {
        public string OutputFolder { get; set; } = "";
        public string TempFolder { get; set; } = "";
        public bool UseSystemTemp { get; set; } = true;

        // Standard oppløsning (720 som standard)
        public string DefaultResolution { get; set; } = "720";

        // Tema: "System", "Light", "Dark"
        public string AppTheme { get; set; } = "Dark";

        // Logging
        public bool EnableLogging { get; set; } = true;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    // Ensure output folder uses correct default if empty
                    if (string.IsNullOrEmpty(settings.OutputFolder))
                    {
                        settings.OutputFolder = NRKLastNed.Mac.PlatformHelper.GetDefaultOutputFolder();
                    }
                    return settings;
                }
                catch { return new AppSettings(); }
            }
            var defaultSettings = new AppSettings();
            if (string.IsNullOrEmpty(defaultSettings.OutputFolder))
            {
                defaultSettings.OutputFolder = NRKLastNed.Mac.PlatformHelper.GetDefaultOutputFolder();
            }
            return defaultSettings;
        }
    }
}