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
            var info = new FfmpegUpdateInfo { IsNewVersionAvailable = false, LatestVersion = "Ukjent" };

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var response = await client.GetStringAsync(RepoUrl);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string published = root.GetProperty("published_at").GetString();
                        DateTime pubDate = DateTime.Parse(published);

                        info.LatestVersion = pubDate.ToString("yyyy-MM-dd");
                        info.PublishedAt = pubDate;

                        // Finn download URL for macOS
                        if (root.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string name = asset.GetProperty("name").GetString();
                                string targetName = PlatformHelper.GetFFmpegDownloadFilename();
                                if (name.Contains(targetName) && !name.Contains("shared"))
                                {
                                    info.DownloadUrl = asset.GetProperty("browser_download_url").GetString();
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
                            if (pubDate > localDate.AddHours(24))
                            {
                                info.IsNewVersionAvailable = true;
                            }
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
                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(info.DownloadUrl);
                    await File.WriteAllBytesAsync(zipPath, data);
                }

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
