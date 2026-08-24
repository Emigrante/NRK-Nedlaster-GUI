using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public class UpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string RepoUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
        private readonly string _toolsPath;
        private readonly string _ytDlpPath;
        private readonly IPlatformService _platform;

        public UpdateService(IPlatformService? platform = null)
        {
            _platform = platform ?? PlatformService.Instance;
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ytDlpPath = _platform.GetToolPath("yt-dlp");

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
            }
        }

        public async Task<string> GetYtDlpVersionAsync()
        {
            if (!File.Exists(_ytDlpPath)) return "Ikke installert";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return "Ukjent";
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return output.Trim();
                }
            }
            catch
            {
                return "Ukjent";
            }
        }

        public async Task<ToolUpdateInfo> CheckForYtDlpUpdateAsync()
        {
            string currentVer = await GetYtDlpVersionAsync();
            var info = new ToolUpdateInfo { CurrentVersion = currentVer, IsNewVersionAvailable = false, LatestVersion = "Ukjent", DownloadUrl = "" };

            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    info.LatestVersion = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "") : "";

                    string targetAsset = _platform.IsWindows ? "yt-dlp.exe" : "yt-dlp";

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                            if (name.Equals(targetAsset, StringComparison.OrdinalIgnoreCase))
                            {
                                info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                break;
                            }
                        }
                    }

                    if (currentVer == "Ikke installert" || currentVer == "Ukjent")
                    {
                        info.IsNewVersionAvailable = true;
                    }
                    else
                    {
                        info.IsNewVersionAvailable = !string.Equals(currentVer, info.LatestVersion, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                info.LatestVersion = "Kunne ikke sjekke";
            }

            return info;
        }

        public async Task<string> UpdateYtDlpAsync(ToolUpdateInfo? info = null)
        {
            if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
            {
                if (!File.Exists(_ytDlpPath)) return "Mangler nedlastings-URL og filen finnes ikke.";

                return await RunInternalUpdate();
            }

            try
            {
                if (!Directory.Exists(_toolsPath)) Directory.CreateDirectory(_toolsPath);

                using (var responseStream = await _httpClient.GetStreamAsync(info.DownloadUrl))
                using (var fileStream = new FileStream(_ytDlpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await responseStream.CopyToAsync(fileStream);
                }

                _platform.SetExecutablePermission(_ytDlpPath);

                return "yt-dlp er lastet ned og oppdatert!";
            }
            catch (Exception ex)
            {
                return $"Feil under nedlasting: {ex.Message}";
            }
        }

        private async Task<string> RunInternalUpdate()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--update");

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return "Kunne ikke starte oppdateringsprosess.";
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return output + Environment.NewLine + error;
                }
            }
            catch (Exception ex)
            {
                return $"Feil: {ex.Message}";
            }
        }
    }
}
