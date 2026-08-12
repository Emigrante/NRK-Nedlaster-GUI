using System;
using System.IO;
using System.Text.Json;
using NRKLastNed.Services;

namespace NRKLastNed.Models
{
    public class AppSettings
    {
        // TV og Radio nedlastingsmapper
        public string TvOutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NRK", "TV");
        public string RadioOutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NRK", "Radio");

        // Alternativ: Bruk samme mappe for både TV og Radio
        public bool UseSameFolderForBoth { get; set; } = false;

        // Legacy - for bakoverkompatibilitet
        public string OutputFolder 
        { 
            get => TvOutputFolder;
            set => TvOutputFolder = value;
        }

        public string TempFolder { get; set; } = "";
        public bool UseSystemTemp { get; set; } = true;

        // Standard oppløsning (720 som standard)
        public string DefaultResolution { get; set; } = "720";

        // Tema: "System", "Light", "Dark"
        public string AppTheme { get; set; } = "Dark";

        // Logging
        public bool EnableLogging { get; set; } = true;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        private static string SettingsPath
        {
            get
            {
                // Lagre i brukerens AppData-mappe i stedet for programmappen
                // Dette gir skriverettigheter uten å trenge admin
                string appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NRKLastNed");
                
                // Opprett mappen hvis den ikke eksisterer
                if (!Directory.Exists(appDataFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(appDataFolder);
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"Kunne ikke opprette innstillingsmappe: {ex.Message}", LogLevel.Error);
                    }
                }
                
                return Path.Combine(appDataFolder, "settings.json");
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                LogService.Log($"Feil ved lagring av innstillinger: {ex.Message}", LogLevel.Error, settings);
            }
        }

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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