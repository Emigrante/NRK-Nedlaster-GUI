using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace NRKLastNed.Mac.Services
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
                            // On macOS, look for .dmg, .pkg, or .zip files
                            if (PlatformHelper.IsMacOS && (assetName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase) || 
                                assetName.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) ||
                                assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                            {
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                fileName = assetName;
                                break;
                            }
                            else if (PlatformHelper.IsWindows && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                fileName = assetName;
                                break;
                            }
                        }
                    }

                    Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

                    string cleanTag = tagName.TrimStart('v', 'V');
                    var parts = cleanTag.Split('.');
                    if (parts.Length == 2 && parts[1].StartsWith("0") && parts[1].Length >= 2)
                    {
                        if (int.TryParse(parts[1], out int minorBuild))
                        {
                            cleanTag = $"{parts[0]}.0.{minorBuild}";
                        }
                    }

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
                // Show error - simplified for now
                Debug.WriteLine("Fant ingen nedlastbar installasjonsfil i denne utgivelsen.");
                return;
            }

            string tempPath = Path.GetTempPath();
            string installerPath = Path.Combine(tempPath, info.FileName ?? "NRKLastNed_Update.zip");

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var data = await client.GetByteArrayAsync(info.DownloadUrl);
                    await File.WriteAllBytesAsync(installerPath, data);
                }

                Debug.WriteLine("Oppdatering lastet ned. Åpne filen for å installere oppdateringen.");
                PlatformHelper.OpenFolder(tempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Feil ved nedlasting: {ex.Message}");

                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }
            }
        }


        public static void ShowReleaseNotesIfJustUpdated()
        {
            // Implementation for showing release notes
        }
    }
}
