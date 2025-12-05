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

        private int _mediaFileCounter = 0;
        private bool _isIgnoringCurrentFile = false;
        private double _maxReportedPercent = 0;
        private double _lastLoggedPercent = -1;

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
                LogService.Log($"Mangler verktøy: {_ytDlpPath}", LogLevel.Error, _settings);
                return false;
            }
            if (!File.Exists(_ffmpegPath))
            {
                message = $"Finner ikke ffmpeg.exe i: {_ffmpegPath}\nOpprett mappen 'Tools' og legg filene der.";
                LogService.Log($"Mangler verktøy: {_ffmpegPath}", LogLevel.Error, _settings);
                return false;
            }
            message = "OK";
            return true;
        }

        public async Task<List<DownloadItem>> AnalyzeUrlAsync(string url)
        {
            LogService.Log($"Starter analyse av URL: {url}", LogLevel.Debug, _settings);

            var items = new List<DownloadItem>();

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = $"-J \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    await Task.WhenAll(outputTask, errorTask);
                    await process.WaitForExitAsync();

                    var output = outputTask.Result;
                    var error = errorTask.Result;

                    if (process.ExitCode != 0)
                    {
                        LogService.Log($"Analyse feilet (ExitCode {process.ExitCode}): {error}", LogLevel.Error, _settings);
                        return items;
                    }

                    LogService.Log("JSON hentet. Parser...", LogLevel.Debug, _settings);

                    using (JsonDocument doc = JsonDocument.Parse(output))
                    {
                        var root = doc.RootElement;

                        if (root.TryGetProperty("_type", out var type) && type.GetString() == "playlist")
                        {
                            if (root.TryGetProperty("entries", out var entries))
                            {
                                foreach (var entry in entries.EnumerateArray())
                                {
                                    var item = ParseJsonEntry(entry, url);
                                    var resolutions = ExtractResolutionsFromJson(entry);
                                    ApplyResolutions(item, resolutions);
                                    items.Add(item);
                                }
                            }
                        }
                        else
                        {
                            var item = ParseJsonEntry(root, url);
                            var resolutions = ExtractResolutionsFromJson(root);
                            ApplyResolutions(item, resolutions);
                            items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"Kritisk feil under analyse: {ex.Message}", LogLevel.Error, _settings);
                var item = new DownloadItem { Url = url, Title = "Kunne ikke analysere tittel", Status = "Feil" };
                items.Add(item);
            }

            LogService.Log($"Analyse ferdig. Fant {items.Count} elementer.", LogLevel.Info, _settings);
            return items;
        }

        private List<string> ExtractResolutionsFromJson(JsonElement element)
        {
            var resolutions = new HashSet<string>();
            resolutions.Add("best");

            if (element.TryGetProperty("formats", out var formats))
            {
                foreach (var format in formats.EnumerateArray())
                {
                    if (format.TryGetProperty("height", out var heightProp) && heightProp.ValueKind == JsonValueKind.Number)
                    {
                        int h = heightProp.GetInt32();
                        if (h > 0) resolutions.Add(h.ToString());
                    }
                }
            }

            var sortedList = new List<string>(resolutions);
            sortedList.Sort((a, b) => {
                if (a == "best") return -1;
                if (b == "best") return 1;
                if (int.TryParse(a, out int ia) && int.TryParse(b, out int ib)) return ib.CompareTo(ia);
                return 0;
            });

            return sortedList;
        }

        private void ApplyResolutions(DownloadItem item, List<string> resolutions)
        {
            foreach (var res in resolutions) item.AvailableResolutions.Add(res);
            string def = _settings.DefaultResolution.Trim();

            if (item.AvailableResolutions.Any(r => r.Trim() == def))
                item.SelectedResolution = item.AvailableResolutions.First(r => r.Trim() == def);
            else if (item.AvailableResolutions.Count > 0)
                item.SelectedResolution = item.AvailableResolutions[0];
            else
                item.SelectedResolution = "best";
        }

        private DownloadItem ParseJsonEntry(JsonElement element, string originalUrl)
        {
            string title = element.TryGetProperty("title", out var t) ? t.GetString() : "Ukjent";
            string url = element.TryGetProperty("webpage_url", out var u) ? u.GetString() : originalUrl;

            string season = element.TryGetProperty("season_number", out var s) ? s.ToString() : "";
            string episode = element.TryGetProperty("episode_number", out var e) ? e.ToString() : "";
            string series = element.TryGetProperty("series", out var ser) ? ser.GetString() : "";

            string cleanTitle = title;
            if (!string.IsNullOrEmpty(series) && cleanTitle.StartsWith(series, StringComparison.OrdinalIgnoreCase))
            {
                cleanTitle = cleanTitle.Substring(series.Length).Trim();
                cleanTitle = Regex.Replace(cleanTitle, @"^[\s-–]+", "");
            }
            cleanTitle = Regex.Replace(cleanTitle, @"^\d+\.\s+", "");

            string displayTitle = cleanTitle;
            string seInfo = "";

            if (!string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(episode))
            {
                seInfo = $"S{int.Parse(season):00}E{int.Parse(episode):00}";
                // ENDRET: Nytt format: Serie - SxxExx - Tittel
                displayTitle = $"{series} - {seInfo} - {cleanTitle}";
            }
            else
            {
                displayTitle = cleanTitle;
            }

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

            // ENDRET: Bruker Title direkte siden den nå allerede inneholder SxxExx på riktig plass
            string fileNameBase = item.Title;

            string resTag = item.SelectedResolution == "best" ? "" : $" - {item.SelectedResolution}p";
            string finalFileName = SanitizeFileName($"{fileNameBase}{resTag}.mkv");
            string fullOutputPath = Path.Combine(tempPath, finalFileName);
            string cleanupBasePattern = SanitizeFileName(fileNameBase);

            string formatSelector = item.SelectedResolution == "best" ? "res" : $"res:{item.SelectedResolution}";
            string langCode = GetLanguageCode(item.SelectedLanguage);

            string metadataArgs = $"--postprocessor-args \"FFmpeg:-metadata:s:a:0 language={langCode}\"";

            string args = $"-N 4 -o \"{fullOutputPath}\" --remux-video mkv -S {formatSelector} --embed-subs --embed-thumbnail --no-mtime --convert-subs srt {metadataArgs} --ffmpeg-location \"{_ffmpegPath}\" --progress --newline \"{item.Url}\"";

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

            LogService.Log($"Starter nedlasting: {item.Title}", LogLevel.Info, _settings);
            LogService.Log($"Kommando: yt-dlp {args}", LogLevel.Debug, _settings);

            _mediaFileCounter = 0;
            _isIgnoringCurrentFile = false;
            _maxReportedPercent = 0;
            _lastLoggedPercent = -1;

            using (var process = new Process { StartInfo = startInfo })
            {
                using (token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            DetectMediaFile(e.Data);
                            ParseProgress(e.Data, progressText, progressPercent);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && _settings.LogLevel == LogLevel.Debug)
                        {
                            if (!e.Data.StartsWith("[download]") && !e.Data.StartsWith("[info]"))
                                LogService.Log($"yt-dlp info: {e.Data.Trim()}", LogLevel.Debug, _settings);
                        }
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
                        LogService.Log($"Avbrutt: {item.Title}", LogLevel.Info, _settings);
                        try { if (!process.HasExited) process.Kill(); } catch { }

                        try
                        {
                            await Task.Delay(500);
                            var filesToDelete = Directory.GetFiles(tempPath, $"{cleanupBasePattern}*");
                            foreach (var file in filesToDelete) try { File.Delete(file); } catch { }
                            LogService.Log("Temp-filer slettet.", LogLevel.Debug, _settings);
                        }
                        catch (Exception ex) { LogService.Log($"Feil v/sletting av temp: {ex.Message}", LogLevel.Error, _settings); }
                        throw;
                    }
                }
            }

            if (token.IsCancellationRequested) return;

            if (File.Exists(fullOutputPath))
            {
                string dest = Path.Combine(_settings.OutputFolder, finalFileName);
                if (File.Exists(dest)) File.Delete(dest);

                bool moved = false;
                for (int i = 0; i < 5; i++)
                {
                    try { File.Move(fullOutputPath, dest); moved = true; break; }
                    catch { await Task.Delay(500); }
                }

                if (moved)
                {
                    LogService.Log($"Ferdig! Lagret til: {dest}", LogLevel.Info, _settings);
                    progressText.Report($"Ferdig");
                    progressPercent.Report(100);
                }
                else
                {
                    LogService.Log($"Kunne ikke flytte fil til {dest}. Fil låst?", LogLevel.Error, _settings);
                    throw new Exception("Fil låst, kunne ikke flytte til output.");
                }
            }
            else
            {
                LogService.Log($"Fant ikke filen etter nedlasting: {fullOutputPath}", LogLevel.Error, _settings);
                throw new Exception("Fil ikke funnet etter nedlasting.");
            }
        }

        private void DetectMediaFile(string line)
        {
            string lowerLine = line.ToLowerInvariant();
            if (lowerLine.Contains("destination:"))
            {
                if (lowerLine.Contains(".jpg") || lowerLine.Contains(".webp") || lowerLine.Contains(".png") || lowerLine.Contains(".vtt") || lowerLine.Contains(".srt") || lowerLine.Contains(".xml"))
                {
                    _isIgnoringCurrentFile = true;
                    return;
                }
                _isIgnoringCurrentFile = false;
                _mediaFileCounter++;
            }
        }

        private void ParseProgress(string line, IProgress<string> text, IProgress<double> percent)
        {
            bool isDownloadLine = line.StartsWith("[download]");

            var match = Regex.Match(line, @"\[download\]\s+(\d+(\.\d+)?)%");
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawPercent))
            {
                if (_isIgnoringCurrentFile) return;

                double calculatedPercent = 0;
                if (_mediaFileCounter <= 1) calculatedPercent = rawPercent * 0.90;
                else calculatedPercent = 90 + (rawPercent * 0.10);

                if (calculatedPercent < _maxReportedPercent) calculatedPercent = _maxReportedPercent;
                else _maxReportedPercent = calculatedPercent;

                if (calculatedPercent > 99) calculatedPercent = 99;

                if (Math.Abs(calculatedPercent - _lastLoggedPercent) >= 1.0 || rawPercent >= 100)
                {
                    percent.Report(calculatedPercent);
                    text.Report($"Laster ned... ({calculatedPercent:0}%)");

                    if (_settings.LogLevel == LogLevel.Debug && isDownloadLine)
                    {
                        LogService.Log($"yt-dlp: {line.Trim()}", LogLevel.Debug, _settings);
                    }

                    _lastLoggedPercent = calculatedPercent;
                }
                return;
            }

            if (line.Contains("[Merger]") || line.Contains("Merging formats") || line.Contains("[VideoRemuxer]") || line.Contains("Writing video"))
            {
                text.Report("Ferdigstiller fil...");
                percent.Report(99);
                LogService.Log($"Ferdigstiller: {line.Trim()}", LogLevel.Debug, _settings);
            }
        }
    }
}