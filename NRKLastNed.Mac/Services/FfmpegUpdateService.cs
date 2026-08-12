using NRKLastNed.Mac.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NRKLastNed.Mac.Services
{
    public class FfmpegUpdateService
    {
        private const string RepoUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";
        private readonly string _toolsPath;

        public class FfmpegUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public DateTime PublishedAt { get; set; }
        }

        private static readonly HttpClient _httpClient;

        static FfmpegUpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
        }

        public FfmpegUpdateService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
        }

        public async Task<string> GetInstalledVersionAsync()
        {
            string exePath = PlatformHelper.GetToolPath("ffmpeg");
            if (!File.Exists(exePath)) return "Ikke installert";

            try
            {
                var fileInfo = new FileInfo(exePath);
                return fileInfo.LastWriteTime.ToString("yyyy-MM-dd");
            }
            catch
            {
                return "Ukjent";
            }
        }

        public async Task<FfmpegUpdateInfo> CheckForUpdatesAsync()
        {
            var info = new FfmpegUpdateInfo { IsNewVersionAvailable = false, LatestVersion = "Ukjent", DownloadUrl = "" };

            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    string published = root.TryGetProperty("published_at", out var pubProp) ? (pubProp.GetString() ?? "") : "";
                    if (DateTime.TryParse(published, out DateTime pubDate))
                    {
                        info.LatestVersion = pubDate.ToString("yyyy-MM-dd");
                        info.PublishedAt = pubDate;
                    }

                    // Finn download URL for macOS
                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                            string targetName = PlatformHelper.GetFFmpegDownloadFilename();
                            if (!string.IsNullOrEmpty(name) && name.Contains(targetName) && !name.Contains("shared"))
                            {
                                info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                break;
                            }
                        }
                    }

                    string localPath = PlatformHelper.GetToolPath("ffmpeg");
                    if (!File.Exists(localPath))
                    {
                        info.IsNewVersionAvailable = true;
                    }
                    else
                    {
                        DateTime localDate = File.GetLastWriteTimeUtc(localPath);
                        if (info.PublishedAt > localDate.AddHours(24))
                        {
                            info.IsNewVersionAvailable = true;
                        }
                    }
                }
            }
            catch
            {
                info.LatestVersion = "Feil ved sjekk";
            }

            return info;
        }

        public async Task UpdateFfmpegAsync(FfmpegUpdateInfo info, IProgress<string> progress)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl)) return;

            string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_update.zip");

            try
            {
                progress.Report("Laster ned...");
                var data = await _httpClient.GetByteArrayAsync(info.DownloadUrl);
                await File.WriteAllBytesAsync(zipPath, data);

                progress.Report("Pakker ut...");
                if (!Directory.Exists(_toolsPath)) Directory.CreateDirectory(_toolsPath);

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string ffmpegName = PlatformHelper.GetToolBinaryName("ffmpeg");
                        string ffprobeName = PlatformHelper.GetToolBinaryName("ffprobe");

                        if (entry.FullName.EndsWith($"bin/{ffmpegName}", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals(ffmpegName, StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = PlatformHelper.GetToolPath("ffmpeg");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);

                            // Make executable on macOS/Linux
                            if (PlatformHelper.IsMacOS || PlatformHelper.IsLinux)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "chmod",
                                        Arguments = $"+x \"{dest}\"",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    })?.WaitForExit();
                                }
                                catch { }
                            }
                        }
                        else if (entry.FullName.EndsWith($"bin/{ffprobeName}", StringComparison.OrdinalIgnoreCase) ||
                                 entry.Name.Equals(ffprobeName, StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = PlatformHelper.GetToolPath("ffprobe");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);

                            // Make executable on macOS/Linux
                            if (PlatformHelper.IsMacOS || PlatformHelper.IsLinux)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "chmod",
                                        Arguments = $"+x \"{dest}\"",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    })?.WaitForExit();
                                }
                                catch { }
                            }
                        }
                    }
                }
                progress.Report("Ferdig!");
            }
            finally
            {
                if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            }
        }
    }
}
