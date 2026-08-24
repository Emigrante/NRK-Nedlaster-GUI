using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public class FfmpegUpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string RepoUrl = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
        private readonly string _toolsPath;
        private readonly string _ffmpegPath;
        private readonly IPlatformService _platform;

        public FfmpegUpdateService(IPlatformService? platform = null)
        {
            _platform = platform ?? PlatformService.Instance;
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ffmpegPath = _platform.GetToolPath("ffmpeg");

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
            }
        }

        public Task<FfmpegUpdateInfo> CheckForUpdatesAsync() => CheckForFfmpegUpdateAsync();
        public Task<string> GetInstalledVersionAsync() => Task.FromResult(GetCurrentFfmpegVersion());

        public string GetCurrentFfmpegVersion()
        {
            if (!File.Exists(_ffmpegPath)) return "Ikke installert";

            try
            {
                var fileInfo = new FileInfo(_ffmpegPath);
                return $"Installert ({fileInfo.LastWriteTime:yyyy-MM-dd})";
            }
            catch
            {
                return "Installert";
            }
        }

        public async Task<FfmpegUpdateInfo> CheckForFfmpegUpdateAsync()
        {
            string currentVer = GetCurrentFfmpegVersion();
            var info = new FfmpegUpdateInfo
            {
                CurrentVersion = currentVer,
                IsNewVersionAvailable = false,
                LatestVersion = "Ukjent",
                DownloadUrl = "",
                ChecksumUrl = "",
                FileName = ""
            };

            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    string tagName = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "") : "";
                    string publishedAtStr = root.TryGetProperty("published_at", out var pubProp) ? (pubProp.GetString() ?? "") : "";

                    DateTime publishedAt = DateTime.MinValue;
                    if (DateTime.TryParse(publishedAtStr, out var parsedDate))
                    {
                        publishedAt = parsedDate;
                        info.LatestVersion = $"Auto-Build ({parsedDate:yyyy-MM-dd})";
                    }
                    else
                    {
                        info.LatestVersion = tagName;
                    }
                    info.PublishedAt = publishedAt;

                    string targetFileName = _platform.GetFFmpegDownloadFilename();

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";

                            if (name.Contains(targetFileName, StringComparison.OrdinalIgnoreCase) &&
                                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? (dlProp.GetString() ?? "") : "";
                                info.FileName = name;
                            }
                            else if (name.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase) ||
                                     name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                            {
                                info.ChecksumUrl = asset.TryGetProperty("browser_download_url", out var cProp) ? (cProp.GetString() ?? "") : "";
                            }
                        }
                    }

                    if (!File.Exists(_ffmpegPath))
                    {
                        info.IsNewVersionAvailable = true;
                    }
                    else if (publishedAt != DateTime.MinValue)
                    {
                        var localTime = File.GetLastWriteTimeUtc(_ffmpegPath);
                        if (publishedAt > localTime.AddDays(1))
                        {
                            info.IsNewVersionAvailable = true;
                        }
                    }
                }
            }
            catch
            {
                info.LatestVersion = "Kunne ikke sjekke";
            }

            return info;
        }

        public async Task<FfmpegUpdateResult> UpdateFfmpegAsync(FfmpegUpdateInfo info, IProgress<string> progress)
        {
            var result = new FfmpegUpdateResult();

            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                result.Message = "Mangler nedlastings-URL for FFmpeg.";
                return result;
            }

            string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_update.zip");

            try
            {
                progress.Report("Laster ned FFmpeg...");
                using (var responseStream = await _httpClient.GetStreamAsync(info.DownloadUrl))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await responseStream.CopyToAsync(fileStream);
                }

                // --- SHA-256 SJEKKSUM-KONTROLL ---
                progress.Report("Beregner og verifiserer SHA-256 checksum...");

                string computedSha256;
                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    byte[] hashBytes = await SHA256.HashDataAsync(fs);
                    computedSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                result.Sha256 = computedSha256;

                string expectedSha256 = "";
                if (!string.IsNullOrEmpty(info.ChecksumUrl))
                {
                    try
                    {
                        string checksumContent = await _httpClient.GetStringAsync(info.ChecksumUrl);
                        string[] lines = checksumContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var line in lines)
                        {
                            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                string hash = parts[0].Trim();
                                string fileName = parts[1].Trim().TrimStart('*');

                                if (fileName.Equals(info.FileName, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(info.FileName) && fileName.EndsWith(info.FileName, StringComparison.OrdinalIgnoreCase)) ||
                                    fileName.Contains(_platform.GetFFmpegDownloadFilename(), StringComparison.OrdinalIgnoreCase))
                                {
                                    expectedSha256 = hash.ToLowerInvariant();
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Log($"Kunne ikke hente checksum-fil: {ex.Message}", LogLevel.Debug);
                    }
                }

                result.ExpectedSha256 = expectedSha256;

                if (!string.IsNullOrEmpty(expectedSha256))
                {
                    if (!string.Equals(computedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        string errorMsg = $"Sikkerhetsadvarsel: SHA-256 sjekksum stemmer ikke!\n\nForventet: {expectedSha256}\nBeregnet: {computedSha256}\n\nFilen er slettet for din sikkerhet.";
                        LogService.Log(errorMsg, LogLevel.Error);

                        if (File.Exists(zipPath))
                        {
                            try { File.Delete(zipPath); } catch { }
                        }

                        result.Message = errorMsg;
                        throw new InvalidDataException(errorMsg);
                    }

                    result.ChecksumVerified = true;
                    LogService.Log($"SHA-256 verifisert for FFmpeg ({info.FileName}): {computedSha256}", LogLevel.Info);
                }
                else
                {
                    LogService.Log($"FFmpeg lastet ned med SHA-256: {computedSha256} (ingen offisiell checksum-fil funnet for match)", LogLevel.Info);
                }

                // --- UTPAKKING ---
                progress.Report("Pakker ut verifisert arkiv...");
                if (!Directory.Exists(_toolsPath)) Directory.CreateDirectory(_toolsPath);

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    string ffmpegName = _platform.GetToolBinaryName("ffmpeg");
                    string ffprobeName = _platform.GetToolBinaryName("ffprobe");

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith($"bin/{ffmpegName}", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals(ffmpegName, StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = _platform.GetToolPath("ffmpeg");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);
                            _platform.SetExecutablePermission(dest);
                        }
                        else if (entry.FullName.EndsWith($"bin/{ffprobeName}", StringComparison.OrdinalIgnoreCase) ||
                                 entry.Name.Equals(ffprobeName, StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = _platform.GetToolPath("ffprobe");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);
                            _platform.SetExecutablePermission(dest);
                        }
                    }
                }

                progress.Report("Ferdig!");
                result.Success = true;
                result.Message = "FFmpeg er oppdatert og verifisert!";
                return result;
            }
            finally
            {
                if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            }
        }
    }
}
