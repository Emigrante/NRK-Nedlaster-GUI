using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public partial class YtDlpService
    {
        public class AnalysisProgressInfo
        {
            public string? StatusMessage { get; set; }
            public string? DetailMessage { get; set; }
            public int ProcessedCount { get; set; }
            public int? TotalCount { get; set; }
            public double ProgressPercent { get; set; }
            public bool IsIndeterminate { get; set; }
            public DownloadItem? Item { get; set; }
        }

        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly AppSettings _settings;
        private readonly IPlatformService _platform;

        private int _mediaFileCounter = 0;
        private bool _isIgnoringCurrentFile = false;
        private double _maxReportedPercent = 0;
        private double _lastLoggedPercent = -1;

        public YtDlpService(AppSettings? settings = null, IPlatformService? platform = null)
        {
            _settings = settings ?? AppSettings.Load();
            _platform = platform ?? PlatformService.Instance;
            _ytDlpPath = _platform.GetToolPath("yt-dlp");
            _ffmpegPath = _platform.GetToolPath("ffmpeg");
        }

        public bool CheckTools(out string missingTool)
        {
            missingTool = "";
            if (!File.Exists(_ytDlpPath))
            {
                missingTool = _ytDlpPath;
                LogService.Log($"Mangler verktoy: {_ytDlpPath}", LogLevel.Error, _settings);
                return false;
            }
            if (!File.Exists(_ffmpegPath))
            {
                missingTool = _ffmpegPath;
                LogService.Log($"Mangler verktoy: {_ffmpegPath}", LogLevel.Error, _settings);
                return false;
            }
            return true;
        }

        public async Task<List<DownloadItem>> AnalyzeUrlAsync(string url, IProgress<AnalysisProgressInfo>? progress = null, CancellationToken cancellationToken = default)
        {
            var items = new List<DownloadItem>();
            bool isTelevision = DetectContentType(url);

            LogService.Log($"Starter analyse av URL: {url} ({(isTelevision ? "TV" : "Radio")})", LogLevel.Debug, _settings);

            progress?.Report(new AnalysisProgressInfo
            {
                StatusMessage = "Analyserer URL...",
                DetailMessage = "Henter oversikt over innhold...",
                IsIndeterminate = true
            });

            var totalCountTask = DiscoverAnalysisTotalCountAsync(url, cancellationToken);

            int processedCount = 0;
            int? knownTotalCount = null;
            var errorLines = new List<string>();

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            startInfo.ArgumentList.Add("--ignore-errors");
            startInfo.ArgumentList.Add("-j");
            startInfo.ArgumentList.Add(url);

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    using (cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true);
                            }
                        }
                        catch { }
                    }))
                    {
                        if (!process.Start())
                        {
                            LogService.Log("Kunne ikke starte yt-dlp for analyse.", LogLevel.Error, _settings);
                            return items;
                        }

                        var outputReadingTask = Task.Run(async () =>
                        {
                            using var reader = process.StandardOutput;
                            string? line;

                            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (knownTotalCount == null && totalCountTask.IsCompletedSuccessfully)
                                {
                                    knownTotalCount = totalCountTask.Result;
                                }

                                try
                                {
                                    using (JsonDocument doc = JsonDocument.Parse(line))
                                    {
                                        var root = doc.RootElement;
                                        var item = ParseJsonToDownloadItem(root, isTelevision);

                                        if (item != null)
                                        {
                                            items.Add(item);
                                            processedCount++;

                                            double percent = 0;
                                            bool isIndeterminate = true;

                                            if (knownTotalCount.HasValue && knownTotalCount.Value > 0)
                                            {
                                                percent = Math.Min(100.0, ((double)processedCount / knownTotalCount.Value) * 100.0);
                                                isIndeterminate = false;
                                            }

                                            string detailText = knownTotalCount.HasValue && knownTotalCount.Value > 0
                                                ? $"Fant {processedCount} av {knownTotalCount.Value}: {item.Title}"
                                                : $"Fant {processedCount}: {item.Title}";

                                            progress?.Report(new AnalysisProgressInfo
                                            {
                                                StatusMessage = $"Analyserer ({processedCount}" + (knownTotalCount.HasValue ? $"/{knownTotalCount.Value})" : ")"),
                                                DetailMessage = detailText,
                                                ProcessedCount = processedCount,
                                                TotalCount = knownTotalCount,
                                                ProgressPercent = percent,
                                                IsIndeterminate = isIndeterminate,
                                                Item = item
                                            });
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogService.Log($"Ugyldig analyse-JSON fra yt-dlp: {ex.Message}", LogLevel.Debug, _settings);
                                }
                            }
                        }, cancellationToken);

                        var errorReadingTask = Task.Run(async () =>
                        {
                            using var reader = process.StandardError;
                            string? line;
                            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    lock (errorLines)
                                    {
                                        errorLines.Add(line.Trim());
                                    }
                                }
                            }
                        }, cancellationToken);

                        await Task.WhenAll(outputReadingTask, errorReadingTask);
                        await process.WaitForExitAsync(cancellationToken);

                        if (knownTotalCount == null)
                        {
                            try
                            {
                                knownTotalCount = await totalCountTask;
                            }
                            catch
                            {
                                knownTotalCount = null;
                            }
                        }

                        if (process.ExitCode != 0 && items.Count == 0)
                        {
                            LogService.Log($"Analyse feilet (ExitCode {process.ExitCode}): {string.Join(Environment.NewLine, errorLines)}", LogLevel.Error, _settings);
                        }
                        else if (process.ExitCode != 0)
                        {
                            LogService.Log($"Analyse fullfort med delvise feil (ExitCode {process.ExitCode}): {string.Join(Environment.NewLine, errorLines)}", LogLevel.Info, _settings);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Log("Analyse avbrutt av bruker.", LogLevel.Info, _settings);
                throw;
            }
            catch (Exception ex)
            {
                LogService.Log($"Kritisk feil under analyse: {ex.Message}", LogLevel.Error, _settings);
                throw;
            }

            LogService.Log($"Analyse ferdig. Fant {items.Count} elementer.", LogLevel.Info, _settings);

            progress?.Report(new AnalysisProgressInfo
            {
                StatusMessage = $"Ferdig. Fant {items.Count} videoer.",
                DetailMessage = items.Count > 0 ? "Klar til nedlasting." : "Ingen videoer funnet.",
                ProcessedCount = items.Count,
                TotalCount = items.Count,
                ProgressPercent = 100,
                IsIndeterminate = false
            });

            return items;
        }

        private async Task<int?> DiscoverAnalysisTotalCountAsync(string url, CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            startInfo.ArgumentList.Add("--flat-playlist");
            startInfo.ArgumentList.Add("--dump-single-json");
            startInfo.ArgumentList.Add(url);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                using var reg = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch { }
                });

                if (!process.Start()) return null;

                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

                using (JsonDocument doc = JsonDocument.Parse(output))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("playlist_count", out var pCountProp) && pCountProp.TryGetInt32(out int pCount) && pCount > 0)
                    {
                        return pCount;
                    }

                    if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                    {
                        int entryCount = entriesProp.GetArrayLength();
                        if (entryCount > 0) return entryCount;
                    }

                    if (root.TryGetProperty("_type", out var typeProp) && typeProp.GetString() == "url")
                    {
                        return 1;
                    }

                    if (root.TryGetProperty("id", out _) || root.TryGetProperty("title", out _))
                    {
                        return 1;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogService.Log($"Kunne ikke hente analysetotal: {ex.Message}", LogLevel.Debug, _settings);
            }

            return null;
        }

        internal DownloadItem? ParseJsonEntry(JsonElement root, string url, bool isTelevision) => ParseJsonToDownloadItem(root, isTelevision);

        internal List<string> ExtractResolutionsFromJson(JsonElement root)
        {
            var resolutions = new HashSet<string>();
            if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in formats.EnumerateArray())
                {
                    if (f.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out int h) && h > 0)
                    {
                        resolutions.Add(h.ToString());
                    }
                }
            }
            return new List<string>(resolutions);
        }

        internal DownloadItem? ParseJsonToDownloadItem(JsonElement root, bool isTelevision)
        {
            string url = root.TryGetProperty("webpage_url", out var uProp) ? (uProp.GetString() ?? "") : "";
            if (string.IsNullOrEmpty(url))
            {
                url = root.TryGetProperty("url", out var u2Prop) ? (u2Prop.GetString() ?? "") : "";
            }

            if (string.IsNullOrEmpty(url)) return null;

            string rawTitle = root.TryGetProperty("title", out var tProp) ? (tProp.GetString() ?? "Ukjent tittel") : "Ukjent tittel";
            string series = root.TryGetProperty("series", out var sProp) ? (sProp.GetString() ?? "") : "";
            int? seasonNum = root.TryGetProperty("season_number", out var snProp) && snProp.TryGetInt32(out int sVal) ? sVal : null;
            int? episodeNum = root.TryGetProperty("episode_number", out var enProp) && enProp.TryGetInt32(out int eVal) ? eVal : null;

            string formattedTitle = rawTitle;
            string seasonEpisodeStr = "";

            if (!string.IsNullOrEmpty(series) && seasonNum.HasValue && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"S{seasonNum:D2}E{episodeNum:D2}";
                string cleanEpisodeTitle = CleanEpisodeTitle(rawTitle, series);

                if (!string.IsNullOrEmpty(cleanEpisodeTitle))
                {
                    formattedTitle = $"{series} - {seasonEpisodeStr} - {cleanEpisodeTitle}";
                }
                else
                {
                    formattedTitle = $"{series} - {seasonEpisodeStr}";
                }
            }
            else if (!string.IsNullOrEmpty(series) && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"E{episodeNum:D2}";
                string cleanEpisodeTitle = CleanEpisodeTitle(rawTitle, series);

                if (!string.IsNullOrEmpty(cleanEpisodeTitle))
                {
                    formattedTitle = $"{series} - {seasonEpisodeStr} - {cleanEpisodeTitle}";
                }
                else
                {
                    formattedTitle = $"{series} - {seasonEpisodeStr}";
                }
            }
            else if (seasonNum.HasValue && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"S{seasonNum:D2}E{episodeNum:D2}";
            }

            var item = new DownloadItem
            {
                Url = url,
                Title = formattedTitle,
                SeasonEpisode = seasonEpisodeStr,
                IsTelevision = isTelevision,
                SelectedResolution = isTelevision ? _settings.DefaultResolution : "best"
            };

            var resolutions = new HashSet<string>();
            var languages = new HashSet<string>();

            if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in formats.EnumerateArray())
                {
                    if (f.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out int h) && h > 0)
                    {
                        resolutions.Add(h.ToString());
                    }

                    if (f.TryGetProperty("language", out var lProp))
                    {
                        string? lang = lProp.GetString();
                        if (!string.IsNullOrEmpty(lang))
                        {
                            string mapped = MapLanguageCode(lang);
                            if (!string.IsNullOrEmpty(mapped)) languages.Add(mapped);
                        }
                    }
                }
            }

            if (resolutions.Count > 0)
            {
                var sortedRes = new List<string>(resolutions);
                sortedRes.Sort((a, b) =>
                {
                    int.TryParse(a, out int ia);
                    int.TryParse(b, out int ib);
                    return ib.CompareTo(ia);
                });

                sortedRes.Insert(0, "best");
                item.AvailableResolutions = new System.Collections.ObjectModel.ObservableCollection<string>(sortedRes);

                if (item.AvailableResolutions.Contains(_settings.DefaultResolution))
                {
                    item.SelectedResolution = _settings.DefaultResolution;
                }
                else
                {
                    item.SelectedResolution = item.AvailableResolutions[0];
                }
            }
            else
            {
                item.AvailableResolutions = new System.Collections.ObjectModel.ObservableCollection<string> { "best", "1080", "720", "480" };
                item.SelectedResolution = "best";
            }

            if (languages.Count > 0)
            {
                string? detected = languages.FirstOrDefault(l => item.AvailableLanguages.Contains(l));
                if (!string.IsNullOrEmpty(detected))
                {
                    item.SelectedLanguage = detected;
                }
            }

            return item;
        }

        private string CleanEpisodeTitle(string rawTitle, string seriesName)
        {
            string clean = rawTitle;

            if (clean.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(seriesName.Length);
            }

            clean = LeadingSeparatorRegex().Replace(clean, "");
            clean = LeadingNumberRegex().Replace(clean, "");

            return clean.Trim();
        }

        private string MapLanguageCode(string code)
        {
            return code.ToLowerInvariant() switch
            {
                "nob" or "nor" or "no" or "nb" or "nn" => "Norsk",
                "swe" or "se" or "sv" => "Svensk",
                "dan" or "dk" or "da" => "Dansk",
                "eng" or "en" or "gb" or "us" => "Engelsk",
                _ => ""
            };
        }

        private bool DetectContentType(string url)
        {
            if (url.Contains("radio.nrk", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private string GetLanguageCode(string languageName)
        {
            return languageName switch
            {
                "Norsk" => "nob",
                "Svensk" => "swe",
                "Dansk" or "Danske" => "dan",
                "Engelsk" => "eng",
                _ => "nob"
            };
        }

        private static string SanitizeFileName(string name)
        {
            return SanitizeFileNameRegex().Replace(name, "_");
        }

        private string GetTargetOutputFolder(bool isTelevision)
        {
            string outputFolder = isTelevision ? _settings.TvOutputFolder : _settings.RadioOutputFolder;
            if (_settings.UseSameFolderForBoth || string.IsNullOrEmpty(outputFolder))
            {
                outputFolder = _settings.TvOutputFolder;
            }
            return outputFolder;
        }

        public async Task DownloadItemAsync(DownloadItem item, IProgress<string> progressText, IProgress<double> progressPercent, CancellationToken token)
        {
            string outputFolder = GetTargetOutputFolder(item.IsTelevision);

            string tempPath = _settings.UseSystemTemp ? Path.Combine(Path.GetTempPath(), "NRKDownload") : _settings.TempFolder;
            if (string.IsNullOrEmpty(tempPath)) tempPath = Path.Combine(Path.GetTempPath(), "NRKDownload");

            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            string fileNameBase = item.Title;

            string fileExtension = item.IsTelevision ? "mkv" : "mp3";
            string resTag = (item.IsTelevision && item.SelectedResolution != "best") ? $" - {item.SelectedResolution}p" : "";
            string finalFileName = SanitizeFileName($"{fileNameBase}{resTag}.{fileExtension}");
            string fullOutputPath = Path.Combine(tempPath, finalFileName);
            string cleanupBasePattern = SanitizeFileName(fileNameBase);

            string formatSelector = item.SelectedResolution == "best" ? "res" : $"res:{item.SelectedResolution}";
            string langCode = GetLanguageCode(item.SelectedLanguage);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            startInfo.ArgumentList.Add("-N");
            startInfo.ArgumentList.Add("4");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(fullOutputPath);

            if (item.IsTelevision)
            {
                startInfo.ArgumentList.Add("--remux-video");
                startInfo.ArgumentList.Add("mkv");
                startInfo.ArgumentList.Add("-S");
                startInfo.ArgumentList.Add(formatSelector);
                startInfo.ArgumentList.Add("--embed-subs");
                startInfo.ArgumentList.Add("--embed-thumbnail");
                startInfo.ArgumentList.Add("--no-mtime");
                startInfo.ArgumentList.Add("--convert-subs");
                startInfo.ArgumentList.Add("srt");
                startInfo.ArgumentList.Add("--postprocessor-args");
                startInfo.ArgumentList.Add($"FFmpeg:-metadata:s:a:0 language={langCode}");
            }
            else
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestaudio");
                startInfo.ArgumentList.Add("--no-mtime");
            }

            startInfo.ArgumentList.Add("--ffmpeg-location");
            startInfo.ArgumentList.Add(_ffmpegPath);
            startInfo.ArgumentList.Add("--progress");
            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add(item.Url);

            LogService.Log($"Starter nedlasting: {item.Title}", LogLevel.Info, _settings);
            LogService.Log($"Kommando: yt-dlp {string.Join(" ", startInfo.ArgumentList)}", LogLevel.Debug, _settings);

            _mediaFileCounter = 0;
            _isIgnoringCurrentFile = false;
            _maxReportedPercent = 0;
            _lastLoggedPercent = -1;

            using (var process = new Process { StartInfo = startInfo })
            {
                using (token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
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
                        try { if (!process.HasExited) process.Kill(true); } catch { }

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
                string dest = Path.Combine(outputFolder, finalFileName);
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
                    LogService.Log($"Kunne ikke flytte fil til {dest}. Fil last?", LogLevel.Error, _settings);
                    throw new Exception("Fil last, kunne ikke flytte til output.");
                }
            }
            else
            {
                LogService.Log($"Fant ikke filen etter nedlasting: {fullOutputPath}", LogLevel.Error, _settings);
                throw new Exception("Fil ikke funnet etter nedlasting.");
            }
        }

        internal void DetectMediaFile(string line)
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

        internal void ParseProgress(string line, IProgress<string> text, IProgress<double> percent)
        {
            bool isDownloadLine = line.StartsWith("[download]");

            var match = DownloadPercentRegex().Match(line);
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

        [GeneratedRegex(@"\[download\]\s+(\d+(\.\d+)?)%")]
        private static partial Regex DownloadPercentRegex();

        [GeneratedRegex(@"^[\s-–]+")]
        private static partial Regex LeadingSeparatorRegex();

        [GeneratedRegex(@"^\d+\.\s+")]
        private static partial Regex LeadingNumberRegex();

        [GeneratedRegex(@"([\\/:*?""<>|\x00-\x1F]*\.+$)|([\\/:*?""<>|\x00-\x1F]+)")]
        private static partial Regex SanitizeFileNameRegex();
    }
}
