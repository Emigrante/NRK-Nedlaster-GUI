using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NRKLastNed.Mac.Services
{
    public class UpdateService
    {
        private readonly string _toolsPath;
        private readonly string _ytDlpPath;
        private const string RepoUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        public class ToolUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string CurrentVersion { get; set; }
            public string DownloadUrl { get; set; }
        }

        private static readonly HttpClient _httpClient;

        static UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
        }

        public UpdateService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ytDlpPath = PlatformHelper.GetToolPath("yt-dlp");
        }

        public async Task<string> GetYtDlpVersionAsync()
        {
            if (!File.Exists(_ytDlpPath)) return "Ikke installert";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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

                    // Finn nedlastings-URL for yt-dlp (macOS uses executable without .exe)
                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                            if (name == "yt-dlp" || (PlatformHelper.IsWindows && name == "yt-dlp.exe"))
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

                var data = await _httpClient.GetByteArrayAsync(info.DownloadUrl);

                await File.WriteAllBytesAsync(_ytDlpPath, data);

                    if (PlatformHelper.IsMacOS || PlatformHelper.IsLinux)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"+x \"{_ytDlpPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            })?.WaitForExit();
                        }
                        catch { }
                    }

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
                Arguments = "--update",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
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
