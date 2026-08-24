using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public class AppUpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string RepoUrl = "https://api.github.com/repos/Emigrante/NRK-Nedlaster-GUI/releases/latest";
        private readonly IPlatformService _platform;
        private readonly IDialogService? _dialogService;

        public AppUpdateService(IPlatformService? platform = null, IDialogService? dialogService = null)
        {
            _platform = platform ?? PlatformService.Instance;
            _dialogService = dialogService;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
            }
        }

        public static void ShowReleaseNotesIfJustUpdated()
        {
            // Placeholder for release notes notification
        }

        public static string FormatUpdatePromptMessage(AppUpdateInfo info)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"En ny versjon er tilgjengelig!");
            sb.AppendLine($"Ny versjon: v{info.LatestVersion} (Din versjon: v{info.CurrentVersion})");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(info.Title) && !info.Title.Trim().Equals($"v{info.LatestVersion}", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Tittel: {info.Title.Trim()}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            {
                sb.AppendLine("Hva er nytt og endret:");
                sb.AppendLine("--------------------------------------------------");
                string notes = info.ReleaseNotes.Trim();
                if (notes.Length > 1500)
                {
                    notes = notes.Substring(0, 1500) + "\n\n... (se GitHub for fullstendig logg)";
                }
                sb.AppendLine(notes);
                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine();
            }

            sb.AppendLine("Vil du laste ned og installere oppdateringen nå?");
            return sb.ToString();
        }

        public string GetCurrentAppVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                if (version != null)
                {
                    return $"{version.Major}.{version.Minor}" + (version.Build > 0 ? $".{version.Build}" : "");
                }

                var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(infoVer))
                {
                    int plusIdx = infoVer.IndexOf('+');
                    return plusIdx > 0 ? infoVer.Substring(0, plusIdx) : infoVer;
                }
            }
            catch { }

            return "1.20";
        }

        public async Task<AppUpdateInfo> CheckForAppUpdatesAsync()
        {
            string currentVer = GetCurrentAppVersion();
            var info = new AppUpdateInfo
            {
                CurrentVersion = currentVer,
                IsNewVersionAvailable = false,
                LatestVersion = currentVer,
                DownloadUrl = "",
                ReleaseNotes = "",
                Title = "",
                FileName = ""
            };

            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;

                    string tagName = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "") : "";
                    info.LatestVersion = tagName.TrimStart('v', 'V');
                    info.Title = root.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? "") : "";
                    info.ReleaseNotes = root.TryGetProperty("body", out var bodyProp) ? (bodyProp.GetString() ?? "") : "";

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";

                            if (_platform.IsWindows && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                info.FileName = name;
                                break;
                            }
                            else if (!_platform.IsWindows && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                info.FileName = name;
                                break;
                            }
                        }
                    }

                    info.IsNewVersionAvailable = IsNewerVersion(info.LatestVersion, currentVer);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"Feil ved sjekk etter app-oppdatering: {ex.Message}", LogLevel.Error);
                info.LatestVersion = "Kunne ikke sjekke";
            }

            return info;
        }

        private bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest) || latest == "Kunne ikke sjekke") return false;

            if (Version.TryParse(NormalizeVersion(latest), out var latestVer) &&
                Version.TryParse(NormalizeVersion(current), out var currentVer))
            {
                return latestVer > currentVer;
            }

            return !string.Equals(latest.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeVersion(string ver)
        {
            ver = ver.TrimStart('v', 'V').Trim();
            int dotCount = 0;
            foreach (char c in ver) if (c == '.') dotCount++;

            if (dotCount == 0) return ver + ".0.0";
            if (dotCount == 1) return ver + ".0";
            return ver;
        }

        public async Task PerformAppUpdateAsync(AppUpdateInfo info, Action? onBeforeExit = null)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowMessageAsync("Fant ingen nedlastbar installasjonsfil i denne utgivelsen.", "Feil", DialogType.Error);
                }
                return;
            }

            string tempPath = Path.GetTempPath();
            string installerPath = Path.Combine(tempPath, info.FileName ?? (_platform.IsWindows ? "NRKLastNed_Setup.exe" : "NRKLastNed_Update.zip"));

            try
            {
                using (var responseStream = await _httpClient.GetStreamAsync(info.DownloadUrl))
                using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await responseStream.CopyToAsync(fileStream);
                }

                if (_platform.IsWindows)
                {
                    string? mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
                    string currentDir = !string.IsNullOrEmpty(mainModulePath) ? (Path.GetDirectoryName(mainModulePath) ?? AppDomain.CurrentDomain.BaseDirectory) : AppDomain.CurrentDomain.BaseDirectory;
                    if (currentDir.EndsWith("\\")) currentDir = currentDir.Substring(0, currentDir.Length - 1);

                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync("Oppdatering lastet ned.\n\nProgrammet lukkes na for a starte installasjonen.", "Oppdatering", DialogType.Info);
                    }

                    string arguments = $"/DIR=\"{currentDir}\"";

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true,
                        Arguments = arguments
                    });

                    onBeforeExit?.Invoke();
                }
                else
                {
                    if (_dialogService != null)
                    {
                        await _dialogService.ShowMessageAsync("Oppdatering lastet ned. Apne mappen for a installere oppdateringen.", "Oppdatering", DialogType.Info);
                    }
                    _platform.OpenFolder(tempPath);
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"Feil ved oppdatering: {ex.Message}", LogLevel.Error);
                if (_dialogService != null)
                {
                    await _dialogService.ShowMessageAsync($"Feil ved start av oppdatering: {ex.Message}", "Feil", DialogType.Error);
                }

                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }
            }
        }
    }
}
