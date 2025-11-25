using NRKLastNed.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NRKLastNed.Services
{
    public class YtDlpService
    {
        private readonly AppSettings _settings;
        private readonly string _toolsPath;
        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;

        // Telle-logikk:
        // _mediaFileCounter = 0 (Start)
        // _mediaFileCounter = 1 (Første fil -> Video 0-80%)
        // _mediaFileCounter = 2 (Andre fil -> Lyd 80-100%)
        private int _mediaFileCounter = 0;
        private bool _isIgnoringCurrentFile = false;
        private double _maxReportedPercent = 0;

        public YtDlpService(AppSettings settings)
        {
            _settings = settings;
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");
            _ffmpegPath = Path.Combine(_toolsPath, "ffmpeg.exe");
        }

        public bool ValidateTools(out string message)
        {
            if (!File.Exists(_ytDlpPath))
            {
                message = $"Finner ikke yt-dlp.exe i: {_ytDlpPath}\nOpprett mappen 'Tools' og legg filene der.";
                LogService.Log("Mangler yt-dlp.exe", LogLevel.Error, _settings);
                return false;
            }
            if (!File.Exists(_ffmpegPath))
            {
                message = $"Finner ikke ffmpeg.exe i: {_ffmpegPath}\nOpprett mappen 'Tools' og legg filene der.";
                LogService.Log("Mangler ffmpeg.exe", LogLevel.Error, _settings);
                return false;
            }
            message = "OK";
            return true;
        }

        private async Task<List<string>> GetResolutionsInternalAsync(string url)
        {
            var resolutions = new HashSet<string>();
            resolutions.Add("best");
            var startInfo = new ProcessStartInfo { FileName = _ytDlpPath, Arguments = $"-F \"{url}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            try { using (var process = Process.Start(startInfo)) { var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); var lines = output.Split('\n'); foreach (var line in lines) { var match = Regex.Match(line, @"\s(\d+x\d+)\s"); if (match.Success) { var resParts = match.Groups[1].Value.Split('x'); if (resParts.Length == 2) resolutions.Add(resParts[1]); } } } } catch (Exception ex) { LogService.Log($"Feil ved henting av formater: {ex.Message}", LogLevel.Error, _settings); }
            var sortedList = new List<string>(resolutions); sortedList.Sort((a, b) => { if (a == "best") return -1; if (b == "best") return 1; int.TryParse(a, out int ia); int.TryParse(b, out int ib); return ib.CompareTo(ia); }); return sortedList;
        }

        public async Task<List<DownloadItem>> AnalyzeUrlAsync(string url)
        {
            LogService.Log($"Starter analyse av: {url}", LogLevel.Info, _settings);
            var items = new List<DownloadItem>();
            var resolutions = await GetResolutionsInternalAsync(url);

            var startInfo = new ProcessStartInfo { FileName = _ytDlpPath, Arguments = $"-J \"{url}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            try { using (var process = Process.Start(startInfo)) { var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); using (JsonDocument doc = JsonDocument.Parse(output)) { var root = doc.RootElement; if (root.TryGetProperty("entries", out var entries)) { foreach (var entry in entries.EnumerateArray()) { var item = ParseJsonEntry(entry, url); ApplyResolutions(item, resolutions); items.Add(item); } } else { var item = ParseJsonEntry(root, url); ApplyResolutions(item, resolutions); items.Add(item); } } } } catch (Exception ex) { LogService.Log($"Feil ved JSON analyse: {ex.Message}", LogLevel.Error, _settings); var item = new DownloadItem { Url = url, Title = "Kunne ikke analysere tittel", Status = "Klar" }; ApplyResolutions(item, resolutions); items.Add(item); }
            return items;
        }

        private void ApplyResolutions(DownloadItem item, List<string> resolutions)
        {
            foreach (var res in resolutions) item.AvailableResolutions.Add(res);
            string def = _settings.DefaultResolution.Trim();
            if (item.AvailableResolutions.Any(r => r.Trim() == def)) item.SelectedResolution = item.AvailableResolutions.First(r => r.Trim() == def);
            else if (item.AvailableResolutions.Count > 0) item.SelectedResolution = item.AvailableResolutions[0];
            else item.SelectedResolution = "best";
        }

        private DownloadItem ParseJsonEntry(JsonElement element, string originalUrl)
        {
            string title = element.TryGetProperty("title", out var t) ? t.GetString() : "Ukjent";
            string url = element.TryGetProperty("url", out var u) ? u.GetString() : originalUrl;
            string season = element.TryGetProperty("season_number", out var s) ? s.ToString() : "";
            string episode = element.TryGetProperty("episode_number", out var e) ? e.ToString() : "";
            string series = element.TryGetProperty("series", out var ser) ? ser.GetString() : "";

            string cleanTitle = title;
            if (!string.IsNullOrEmpty(series) && cleanTitle.StartsWith(series, StringComparison.OrdinalIgnoreCase)) { cleanTitle = cleanTitle.Substring(series.Length).Trim(); cleanTitle = Regex.Replace(cleanTitle, @"^[\s-–]+", ""); }
            cleanTitle = Regex.Replace(cleanTitle, @"^\d+\.\s+", "");

            string displayTitle = cleanTitle; string seInfo = "";
            if (!string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(episode)) { seInfo = $"S{int.Parse(season):00}E{int.Parse(episode):00}"; displayTitle = $"{series} - {cleanTitle}"; }
            else { displayTitle = cleanTitle; }

            return new DownloadItem { Url = url, Title = displayTitle, SeasonEpisode = seInfo, Status = "Klar", Progress = 0 };
        }

        private string GetLanguageCode(string languageName)
        {
            return languageName switch { "Norsk" => "nob", "Svensk" => "swe", "Dansk" => "dan", "Engelsk" => "eng", _ => "und" };
        }

        private string SanitizeFileName(string name)
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return Regex.Replace(name, invalidRegStr, "_");
        }

        public async Task DownloadItemAsync(DownloadItem item, IProgress<string> progressText, IProgress<double> progressPercent, CancellationToken token)
        {
            string tempPath = _settings.UseSystemTemp ? Path.Combine(Path.GetTempPath(), "NRKDownload") : _settings.TempFolder;
            if (string.IsNullOrEmpty(tempPath)) tempPath = Path.Combine(Path.GetTempPath(), "NRKDownload");

            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
            if (!Directory.Exists(_settings.OutputFolder)) Directory.CreateDirectory(_settings.OutputFolder);

            LogService.Log($"Starter nedlasting av {item.Title}", LogLevel.Info, _settings);

            string fileNameBase = !string.IsNullOrEmpty(item.SeasonEpisode) ? $"{item.Title} - {item.SeasonEpisode}" : item.Title;
            string resTag = item.SelectedResolution == "best" ? "" : $" - {item.SelectedResolution}p";
            string finalFileName = SanitizeFileName($"{fileNameBase}{resTag}.mkv");
            string fullOutputPath = Path.Combine(tempPath, finalFileName);

            // Denne basen brukes for sletting ved avbryt (f.eks "Film tittel - S01E01")
            string cleanupBasePattern = SanitizeFileName(fileNameBase);

            string formatSelector = item.SelectedResolution == "best" ? "res" : $"res:{item.SelectedResolution}";
            string langCode = GetLanguageCode(item.SelectedLanguage);
            string metadataArgs = $"--postprocessor-args \"FFmpeg:-metadata:s:a:0 language={langCode}\"";

            string args = $"-o \"{fullOutputPath}\" --remux-video mkv -S {formatSelector} --embed-subs --embed-thumbnail --no-mtime --convert-subs srt {metadataArgs} --ffmpeg-location \"{_ffmpegPath}\" --progress --newline \"{item.Url}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            // RESET TILSTAND
            _mediaFileCounter = 0;
            _isIgnoringCurrentFile = false;
            _maxReportedPercent = 0;

            using (var process = new Process { StartInfo = startInfo })
            {
                using (token.Register(() => {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                }))
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogService.Log($"[yt-dlp] {e.Data}", LogLevel.Debug, _settings);
                            DetectMediaFile(e.Data);
                            ParseProgress(e.Data, progressText, progressPercent);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) LogService.Log($"[yt-dlp stderr] {e.Data}", LogLevel.Debug, _settings);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    try
                    {
                        await process.WaitForExitAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        // --- FORBEDRET SLETTING VED AVBRYT ---
                        // Vi finner alle filer i temp-mappen som starter med samme navn som filen vi lastet ned.
                        // Dette tar knekken på .part, .f1234.mp4, .jpg, .vtt osv.
                        try
                        {
                            var filesToDelete = Directory.GetFiles(tempPath, $"{cleanupBasePattern}*");
                            foreach (var file in filesToDelete)
                            {
                                try { File.Delete(file); } catch { /* Låst fil? */ }
                            }
                            LogService.Log($"Ryddet opp midlertidige filer for {cleanupBasePattern}", LogLevel.Info, _settings);
                        }
                        catch (Exception ex)
                        {
                            LogService.Log($"Feil under opprydding: {ex.Message}", LogLevel.Error, _settings);
                        }
                        throw; // Kast videre slik at UI oppdateres
                    }
                }
            }

            if (token.IsCancellationRequested) return;

            if (File.Exists(fullOutputPath))
            {
                await Task.Delay(500);
                string dest = Path.Combine(_settings.OutputFolder, finalFileName);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(fullOutputPath, dest);
                progressText.Report($"Ferdig");
                progressPercent.Report(100);
            }
            else
            {
                progressText.Report($"Feilet");
                throw new Exception("Fil ikke funnet etter nedlasting.");
            }
        }

        private void DetectMediaFile(string line)
        {
            string lowerLine = line.ToLowerInvariant();

            // Vi ser etter linjer som: [download] Destination: ...
            if (lowerLine.Contains("destination:"))
            {
                // 1. SJEKK OM VI SKAL IGNORERE (Bilder, tekst, XML)
                if (lowerLine.Contains(".jpg") || lowerLine.Contains(".webp") || lowerLine.Contains(".png") || lowerLine.Contains(".vtt") || lowerLine.Contains(".srt") || lowerLine.Contains(".xml"))
                {
                    _isIgnoringCurrentFile = true;
                    LogService.Log("Ignorerer hjelpefil", LogLevel.Debug, _settings);
                    return;
                }

                // 2. HVIS IKKE IGNORERT -> DET ER EN MEDIAFIL (Video eller Lyd)
                // Vi teller bare oppover.
                // Fil 1 = Video (0-80%)
                // Fil 2 = Lyd (80-100%)
                _isIgnoringCurrentFile = false;
                _mediaFileCounter++;

                LogService.Log($"Startet på mediafil #{_mediaFileCounter}", LogLevel.Debug, _settings);
            }
        }

        private void ParseProgress(string line, IProgress<string> text, IProgress<double> percent)
        {
            var match = Regex.Match(line, @"\[download\]\s+(\d+(\.\d+)?)%");

            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawPercent))
            {
                // Hvis vi ignorerer filen (bilde/tekst), gjør ingenting med baren
                if (_isIgnoringCurrentFile) return;

                double calculatedPercent = 0;

                // TELLE-METODEN:
                if (_mediaFileCounter <= 1)
                {
                    // Første fil (Video): 0-80%
                    calculatedPercent = rawPercent * 0.80;
                }
                else
                {
                    // Andre fil (Lyd) eller mer: 80-100%
                    calculatedPercent = 80 + (rawPercent * 0.20);
                }

                // Visuell glatting
                if (calculatedPercent < _maxReportedPercent) calculatedPercent = _maxReportedPercent;
                else _maxReportedPercent = calculatedPercent;

                if (calculatedPercent > 99) calculatedPercent = 99;

                percent.Report(calculatedPercent);
                text.Report($"Laster ned... ({calculatedPercent:0}%)");
            }

            // MERGING / FERDIGSTILLING
            if (line.Contains("[Merger]") || line.Contains("Merging formats") || line.Contains("[VideoRemuxer]") || line.Contains("Writing video"))
            {
                text.Report("Ferdigstiller fil...");
                percent.Report(100);
            }
        }
    }
}