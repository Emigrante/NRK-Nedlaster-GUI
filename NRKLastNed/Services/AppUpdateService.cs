using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NRKLastNed.Services
{
    public class AppUpdateService
    {
        private const string RepoOwner = "Emigrante";
        private const string RepoName = "NRK-Nedlaster-GUI";

        public class AppUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string CurrentVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
            public string Title { get; set; }
            public string FileName { get; set; }
        }

        private static readonly HttpClient _httpClient;

        static AppUpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
        }

        public async Task<AppUpdateInfo> CheckForAppUpdatesAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var response = await _httpClient.GetStringAsync(url);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    string tagName = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "") : "";
                    string body = root.TryGetProperty("body", out var bodyProp) ? (bodyProp.GetString() ?? "") : "";
                    string name = root.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? "") : "";

                    string downloadUrl = "";
                    string fileName = "";

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string assetName = asset.TryGetProperty("name", out var aNameProp) ? (aNameProp.GetString() ?? "") : "";
                            if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                fileName = assetName;
                                break;
                            }
                        }
                    }

                    // Hent lokal versjon
                    Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

                    // Parse GitHub tag (f.eks "v1.03")
                    string cleanTag = tagName.TrimStart('v', 'V');

                    // SPESIALHÅNDTERING FOR FORMATET v1.03, v1.04 osv.
                    var parts = cleanTag.Split('.');
                    if (parts.Length == 2 && parts[1].StartsWith("0") && parts[1].Length >= 2)
                    {
                        if (int.TryParse(parts[1], out int minorBuild))
                        {
                            cleanTag = $"{parts[0]}.0.{minorBuild}";
                        }
                    }

                    // Fallback: Hvis taggen mangler punktum helt
                    if (cleanTag.Split('.').Length < 2) cleanTag += ".0";
                    if (cleanTag.Split('.').Length < 3) cleanTag += ".0";

                    if (Version.TryParse(cleanTag, out Version? latestVersion) && latestVersion != null)
                    {
                        bool updateAvailable = latestVersion > currentVersion;
                        return new AppUpdateInfo
                        {
                            IsNewVersionAvailable = updateAvailable,
                            LatestVersion = tagName,
                            CurrentVersion = $"v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}",
                            DownloadUrl = downloadUrl,
                            ReleaseNotes = body,
                            Title = name,
                            FileName = fileName
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Feil ved sjekk av oppdatering: " + ex.Message);
            }

            return new AppUpdateInfo { IsNewVersionAvailable = false, LatestVersion = "", CurrentVersion = "", DownloadUrl = "", ReleaseNotes = "", Title = "", FileName = "" };
        }

        public async Task PerformAppUpdateAsync(AppUpdateInfo info)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                MessageBox.Show("Fant ingen nedlastbar installasjonsfil i denne utgivelsen.", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string tempPath = Path.GetTempPath();
            string installerPath = Path.Combine(tempPath, info.FileName ?? "NRKLastNed_Setup.exe");

            try
            {
                var data = await _httpClient.GetByteArrayAsync(info.DownloadUrl);
                await File.WriteAllBytesAsync(installerPath, data);

                string currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                if (currentDir.EndsWith("\\")) currentDir = currentDir.Substring(0, currentDir.Length - 1);

                MessageBox.Show("Oppdatering lastet ned.\n\nProgrammet lukkes nå for å starte installasjonen.",
                                "Oppdatering", MessageBoxButton.OK, MessageBoxImage.Information);

                // Tving installasjon til nåværende mappe
                string arguments = $"/DIR=\"{currentDir}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Arguments = arguments
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Feil ved start av oppdatering: {ex.Message}", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);

                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }
            }
        }

        public static void ShowReleaseNotesIfJustUpdated()
        {
        }
    }
}