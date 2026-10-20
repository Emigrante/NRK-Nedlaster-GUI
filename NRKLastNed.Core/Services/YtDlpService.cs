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
        private int _lastLoggedDecade = 0;
        private bool _hasLoggedMerger = false;
        private string _lastObservedSpeed = "";

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
            string ffprobePath = _platform.GetToolPath("ffprobe");
            if (!File.Exists(ffprobePath))
            {
                missingTool = ffprobePath;
                LogService.Log($"Mangler verktoy: {ffprobePath}", LogLevel.Error, _settings);
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

            using var totalCountCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var totalCountTask = DiscoverAnalysisTotalCountAsync(url, totalCountCts.Token);

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
                            totalCountCts.Cancel();
                            return items;
                        }

                        var outputReadingTask = Task.Run(async () =>
                        {
                            using var reader = process.StandardOutput;
                            string? line;

                            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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

                                        // Hent totalt antall direkte fra JSON-strømmen hvis tilgjengelig, og avbryt ekstra-prosessen
                                        if (knownTotalCount == null)
                                        {
                                            if (root.TryGetProperty("playlist_count", out var pcProp) && pcProp.TryGetInt32(out int pc) && pc > 0)
                                            {
                                                knownTotalCount = pc;
                                                totalCountCts.Cancel();
                                            }
                                            else if (root.TryGetProperty("n_entries", out var neProp) && neProp.TryGetInt32(out int ne) && ne > 0)
                                            {
                                                knownTotalCount = ne;
                                                totalCountCts.Cancel();
                                            }
                                        }

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
                            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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

                        await Task.WhenAll(outputReadingTask, errorReadingTask).ConfigureAwait(false);
                        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                        // Hovedprosessen er ferdig – avbryt bakgrunnssjekken hvis den fortsatt kjører, så vi ikke venter unødvendig
                        totalCountCts.Cancel();

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
                totalCountCts.Cancel();
                LogService.Log("Analyse avbrutt av bruker.", LogLevel.Info, _settings);
                throw;
            }
            catch (Exception ex)
            {
                totalCountCts.Cancel();
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
                RedirectStandardError = false,
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

                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

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
            string episodeJson = root.TryGetProperty("episode", out var epProp) ? (epProp.GetString() ?? "") : "";
            int? seasonNum = root.TryGetProperty("season_number", out var snProp) && snProp.TryGetInt32(out int sVal) ? sVal : null;
            int? episodeNum = root.TryGetProperty("episode_number", out var enProp) && enProp.TryGetInt32(out int eVal) ? eVal : null;

            // Fallback for sesong og episode med forhåndskompilerte GeneratedRegex (null ekstra allokering)
            if (!seasonNum.HasValue || !episodeNum.HasValue)
            {
                var seMatch = ShortSeasonEpisodeRegex().Match(rawTitle);
                if (!seMatch.Success && !string.IsNullOrEmpty(url))
                    seMatch = ShortSeasonEpisodeRegex().Match(url);

                if (!seMatch.Success)
                    seMatch = LongSeasonEpisodeRegex().Match(rawTitle);

                if (!seMatch.Success && !string.IsNullOrEmpty(url))
                    seMatch = UrlSeasonEpisodeRegex().Match(url);

                if (seMatch.Success)
                {
                    if (!seasonNum.HasValue && int.TryParse(seMatch.Groups[1].ValueSpan, out int sParsed))
                        seasonNum = sParsed;
                    if (!episodeNum.HasValue && int.TryParse(seMatch.Groups[2].ValueSpan, out int eParsed))
                        episodeNum = eParsed;

                    if (string.IsNullOrEmpty(series) && seMatch.Index > 0)
                    {
                        series = rawTitle[..seMatch.Index].Trim(' ', '-', ':', '.');
                    }
                }
            }

            if (!episodeNum.HasValue)
            {
                var epOnlyMatch = EpisodePrefixRegex().Match(rawTitle);
                if (!epOnlyMatch.Success)
                    epOnlyMatch = LeadingNumberCaptureRegex().Match(rawTitle);

                if (epOnlyMatch.Success && int.TryParse(epOnlyMatch.Groups[1].ValueSpan, out int eParsed))
                {
                    episodeNum = eParsed;
                }
            }

            string formattedTitle = rawTitle;
            string seasonEpisodeStr = "";

            if (!string.IsNullOrEmpty(series) && seasonNum.HasValue && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"S{seasonNum:D2}E{episodeNum:D2}";
                string cleanEpisodeTitle = CleanEpisodeTitle(rawTitle, series);

                formattedTitle = !string.IsNullOrEmpty(cleanEpisodeTitle)
                    ? $"{series} - {seasonEpisodeStr} - {cleanEpisodeTitle}"
                    : $"{series} - {seasonEpisodeStr}";
            }
            else if (!string.IsNullOrEmpty(series) && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"E{episodeNum:D2}";
                string cleanEpisodeTitle = CleanEpisodeTitle(rawTitle, series);

                formattedTitle = !string.IsNullOrEmpty(cleanEpisodeTitle)
                    ? $"{series} - {seasonEpisodeStr} - {cleanEpisodeTitle}"
                    : $"{series} - {seasonEpisodeStr}";
            }
            else if (seasonNum.HasValue && episodeNum.HasValue)
            {
                seasonEpisodeStr = $"S{seasonNum:D2}E{episodeNum:D2}";
            }

            bool itemIsTelevision = isTelevision;
            if (!string.IsNullOrEmpty(url))
            {
                if (url.Contains("radio.nrk", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("nrk.no/radio", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("nrk.no/podkast", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("podkast.nrk", StringComparison.OrdinalIgnoreCase))
                {
                    itemIsTelevision = false;
                }
            }

            if (root.TryGetProperty("playlist_webpage_url", out var pwProp))
            {
                string? pwUrl = pwProp.GetString();
                if (!string.IsNullOrEmpty(pwUrl) && (pwUrl.Contains("radio.nrk", StringComparison.OrdinalIgnoreCase) || pwUrl.Contains("/podkast", StringComparison.OrdinalIgnoreCase)))
                {
                    itemIsTelevision = false;
                }
            }

            if (root.TryGetProperty("extractor", out var exProp))
            {
                string? extractor = exProp.GetString();
                if (extractor != null && extractor.Contains("radio", StringComparison.OrdinalIgnoreCase))
                {
                    itemIsTelevision = false;
                }
            }

            if (root.TryGetProperty("vcodec", out var vcProp) && vcProp.GetString() == "none" &&
                root.TryGetProperty("ext", out var extProp) && extProp.GetString() == "mp3")
            {
                itemIsTelevision = false;
            }

            string seriesTitle = "";
            string episodeTitle = "";

            if (!itemIsTelevision)
            {
                seriesTitle = !string.IsNullOrWhiteSpace(series) ? series : "";
                if (string.IsNullOrWhiteSpace(seriesTitle))
                {
                    if (root.TryGetProperty("channel", out var chProp) && chProp.GetString() is { Length: > 0 } ch)
                        seriesTitle = ch;
                    else if (root.TryGetProperty("uploader", out var upProp) && upProp.GetString() is { Length: > 0 } up)
                        seriesTitle = up;
                    else if (rawTitle.Contains(" - "))
                    {
                        var parts = rawTitle.Split(new[] { " - " }, 2, StringSplitOptions.TrimEntries);
                        seriesTitle = parts[0];
                        episodeTitle = parts[1];
                    }
                    else
                    {
                        seriesTitle = rawTitle;
                    }
                }

                if (string.IsNullOrWhiteSpace(episodeTitle))
                {
                    string candidate = !string.IsNullOrWhiteSpace(episodeJson) ? episodeJson : CleanEpisodeTitle(rawTitle, seriesTitle);
                    if (!string.Equals(candidate, seriesTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        episodeTitle = candidate;
                    }
                }
            }
            else
            {
                seriesTitle = !string.IsNullOrWhiteSpace(series) ? series : rawTitle;
                string cleanEp = !string.IsNullOrWhiteSpace(episodeJson) ? episodeJson : CleanEpisodeTitle(rawTitle, seriesTitle);
                if (!string.Equals(cleanEp, seriesTitle, StringComparison.OrdinalIgnoreCase))
                {
                    episodeTitle = cleanEp;
                }
            }

            string detectedLang = DetectLanguage(root);

            var item = new DownloadItem
            {
                Url = url,
                Title = formattedTitle,
                SeriesTitle = seriesTitle,
                EpisodeTitle = episodeTitle,
                SeasonEpisode = seasonEpisodeStr,
                IsTelevision = itemIsTelevision,
                SelectedResolution = itemIsTelevision ? _settings.DefaultResolution : "best",
                SelectedLanguage = detectedLang,
                SelectedCodec = itemIsTelevision ? _settings.DefaultCodec : VideoCodecs.None
            };

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

            item.UpdateFileName();
            return item;
        }

        private string DetectLanguage(JsonElement root)
        {
            if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in formats.EnumerateArray())
                {
                    if (f.TryGetProperty("language", out var fLangProp))
                    {
                        string? flang = fLangProp.GetString();
                        if (!string.IsNullOrEmpty(flang))
                        {
                            string mapped = MapLanguageCode(flang);
                            if (!string.IsNullOrEmpty(mapped)) return mapped;
                        }
                    }

                    if (f.TryGetProperty("format_note", out var fnProp))
                    {
                        string? note = fnProp.GetString();
                        if (!string.IsNullOrEmpty(note))
                        {
                            string mapped = MapLanguageText(note);
                            if (!string.IsNullOrEmpty(mapped)) return mapped;
                        }
                    }

                    if (f.TryGetProperty("format_id", out var fidProp))
                    {
                        string? fid = fidProp.GetString();
                        if (!string.IsNullOrEmpty(fid))
                        {
                            string mapped = MapLanguageText(fid);
                            if (!string.IsNullOrEmpty(mapped)) return mapped;
                        }
                    }
                }
            }

            if (root.TryGetProperty("language", out var langProp))
            {
                string? lang = langProp.GetString();
                if (!string.IsNullOrEmpty(lang))
                {
                    string mapped = MapLanguageCode(lang);
                    if (!string.IsNullOrEmpty(mapped)) return mapped;
                }
            }

            string description = root.TryGetProperty("description", out var dProp) ? (dProp.GetString() ?? "") : "";
            string title = root.TryGetProperty("title", out var tProp) ? (tProp.GetString() ?? "") : "";
            string series = root.TryGetProperty("series", out var sProp) ? (sProp.GetString() ?? "") : "";
            string combinedText = $"{series} {title} {description}";

            if (combinedText.Contains("svensk serie", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("svensk drama", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("svensk krim", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("fra sverige", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("svensk humordrama", StringComparison.OrdinalIgnoreCase))
            {
                return "Svensk";
            }
            if (combinedText.Contains("dansk serie", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("dansk drama", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("dansk krim", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("fra danmark", StringComparison.OrdinalIgnoreCase))
            {
                return "Dansk";
            }
            if (combinedText.Contains("britisk", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("amerikansk", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("engelsk", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("australsk", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("fra usa", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("fra uk", StringComparison.OrdinalIgnoreCase) ||
                combinedText.Contains("fra storbritannia", StringComparison.OrdinalIgnoreCase))
            {
                return "Engelsk";
            }

            return "Norsk";
        }

        private string MapLanguageText(string text)
        {
            if (text.Contains("norsk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("norwegian", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("bokmål", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("nynorsk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-nob", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-nor", StringComparison.OrdinalIgnoreCase))
                return "Norsk";

            if (text.Contains("svensk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("swedish", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-swe", StringComparison.OrdinalIgnoreCase))
                return "Svensk";

            if (text.Contains("dansk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("danish", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-dan", StringComparison.OrdinalIgnoreCase))
                return "Dansk";

            if (text.Contains("engelsk", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("english", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-eng", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("-en", StringComparison.OrdinalIgnoreCase))
                return "Engelsk";

            return "";
        }

        private string CleanEpisodeTitle(string rawTitle, string seriesName)
        {
            string clean = rawTitle;

            if (!string.IsNullOrEmpty(seriesName) && clean.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(seriesName.Length);
            }
            else if (clean.Contains(" - "))
            {
                var parts = clean.Split(new[] { " - " }, 2, StringSplitOptions.TrimEntries);
                if (!string.IsNullOrEmpty(seriesName) && (seriesName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || parts[0].StartsWith(seriesName, StringComparison.OrdinalIgnoreCase)))
                {
                    clean = parts[1];
                }
            }

            clean = LeadingSeparatorRegex().Replace(clean, "");
            clean = LeadingNumberRegex().Replace(clean, "");

            return clean.Trim();
        }

        private string MapLanguageCode(string code)
        {
            return code.ToLowerInvariant().Trim() switch
            {
                "nob" or "nor" or "no" or "nb" or "nn" => "Norsk",
                "swe" or "se" or "sv" => "Svensk",
                "dan" or "dk" or "da" => "Dansk",
                "eng" or "en" or "gb" or "us" or "uk" => "Engelsk",
                _ => MapLanguageText(code)
            };
        }

        private bool DetectContentType(string url)
        {
            if (url.Contains("radio.nrk", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("nrk.no/radio", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("nrk.no/podkast", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("podkast.nrk", StringComparison.OrdinalIgnoreCase))
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

        public Task DownloadItemAsync(DownloadItem item, IProgress<string> progressText, IProgress<double> progressPercent, CancellationToken token)
            => DownloadItemAsync(item, progressText, progressPercent, null, null, null, token, 0, 0);

        public Task DownloadItemAsync(DownloadItem item, IProgress<string> progressText, IProgress<double> progressPercent, IProgress<string>? rawOutputProgress, CancellationToken token)
            => DownloadItemAsync(item, progressText, progressPercent, rawOutputProgress, null, null, token, 0, 0);

        public async Task DownloadItemAsync(
            DownloadItem item,
            IProgress<string> progressText,
            IProgress<double> progressPercent,
            IProgress<string>? rawOutputProgress,
            IProgress<double>? downloadStepProgress,
            IProgress<double>? processingStepProgress,
            CancellationToken token,
            int fileIndex = 0,
            int totalFiles = 0)
        {
            var activeSettings = AppSettings.Load();
            _settings.DownloadLogLevel = activeSettings.DownloadLogLevel;
            _settings.UseHardwareAcceleration = activeSettings.UseHardwareAcceleration;

            bool shouldReencode = item.IsTelevision && VideoCodecs.IsReencoding(item.SelectedCodec);

            // Hvis vi skal rekode, sørg for at GPU-deteksjon allerede kjører i bakgrunnen MENS vi laster ned!
            if (shouldReencode)
            {
                _ = HardwareEncoderService.GetCapabilitiesAsync(_ffmpegPath);
            }

            string outputFolder = GetTargetOutputFolder(item.IsTelevision);

            string tempPath = _settings.UseSystemTemp ? Path.Combine(Path.GetTempPath(), "NRKDownload") : _settings.TempFolder;
            if (string.IsNullOrEmpty(tempPath)) tempPath = Path.Combine(Path.GetTempPath(), "NRKDownload");

            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            string finalFileName = FileNameFormatter.BuildOutputFileName(item);
            item.FileName = finalFileName;
            string fullOutputPath = Path.Combine(tempPath, finalFileName);
            string cleanupBasePattern = Path.GetFileNameWithoutExtension(finalFileName);

            string formatSelector = item.SelectedResolution == "best" ? "res" : $"res:{item.SelectedResolution}";
            string langCode = GetLanguageCode(item.SelectedLanguage);

            downloadStepProgress?.Report(0);
            processingStepProgress?.Report(0);

            IProgress<double> downloadPercent = shouldReencode
                ? new ScaledProgress(progressPercent, 0, DownloadShareWhenReencoding)
                : progressPercent;

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            // 8 samtidige fragmenter gir merkbart raskere HLS-nedlasting og ferdigstillelse fra NRK
            startInfo.ArgumentList.Add("-N");
            startInfo.ArgumentList.Add("8");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(fullOutputPath);

            if (item.IsTelevision)
            {
                // VIKTIG OPTIMALISERING: --merge-output-format mkv gjør at yt-dlp fletter direkte til .mkv
                // i stedet for først å kjøre FixupM3u8 + flette til .mp4 + deretter kjøre VideoRemuxer til .mkv!
                startInfo.ArgumentList.Add("--merge-output-format");
                startInfo.ArgumentList.Add("mkv");
                startInfo.ArgumentList.Add("--remux-video");
                startInfo.ArgumentList.Add("mkv");
                startInfo.ArgumentList.Add("-S");
                startInfo.ArgumentList.Add(formatSelector);
                startInfo.ArgumentList.Add("--write-subs");
                startInfo.ArgumentList.Add("--sub-langs");
                startInfo.ArgumentList.Add("all");
                startInfo.ArgumentList.Add("--embed-subs");
                startInfo.ArgumentList.Add("--embed-thumbnail");
                startInfo.ArgumentList.Add("--no-mtime");
                startInfo.ArgumentList.Add("--convert-subs");
                startInfo.ArgumentList.Add("srt");
                startInfo.ArgumentList.Add("--postprocessor-args");
                startInfo.ArgumentList.Add($"Merger+VideoRemuxer:-metadata:s:a:0 language={langCode}");
            }
            else
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestaudio");
                startInfo.ArgumentList.Add("-x");
                startInfo.ArgumentList.Add("--audio-format");
                startInfo.ArgumentList.Add("mp3");
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
            _lastLoggedDecade = 0;
            _hasLoggedMerger = false;
            _lastObservedSpeed = "";

            string filePrefix = (fileIndex > 0 && totalFiles > 1) ? $"• [Fil {fileIndex}/{totalFiles}] " : "• ";
            rawOutputProgress?.Report($"{filePrefix}Laster ned fra NRK: {item.Title}");

            var downloadStopwatch = Stopwatch.StartNew();

            using (var process = new Process { StartInfo = startInfo })
            {
                using (token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            DetectMediaFile(e.Data);
                            ParseProgress(e.Data, progressText, downloadPercent, downloadStepProgress, rawOutputProgress);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            string trimmed = e.Data.Trim();
                            if (trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                            {
                                rawOutputProgress?.Report($"✖ Feil under nedlasting: {trimmed}");
                                LogService.Log($"yt-dlp feil: {trimmed}", LogLevel.Error, _settings);
                            }
                            else if (_settings.LogLevel == LogLevel.Debug &&
                                     !trimmed.StartsWith("[download]", StringComparison.Ordinal) &&
                                     !trimmed.StartsWith("[info]", StringComparison.Ordinal))
                            {
                                LogService.Log($"yt-dlp info: {trimmed}", LogLevel.Debug, _settings);
                            }
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    try
                    {
                        await process.WaitForExitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        LogService.Log($"Avbrutt: {item.Title}", LogLevel.Info, _settings);
                        try { if (!process.HasExited) process.Kill(true); } catch { }

                        try
                        {
                            await Task.Delay(500).ConfigureAwait(false);
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

            downloadStopwatch.Stop();
            downloadStepProgress?.Report(100);

            string downloadDurationStr = FormatDuration(downloadStopwatch.Elapsed);
            string speedReport = !string.IsNullOrEmpty(_lastObservedSpeed) ? $" (snitt {_lastObservedSpeed})" : "";
            rawOutputProgress?.Report($"  ↳ Nedlastet på {downloadDurationStr}{speedReport}");

            if (shouldReencode && File.Exists(fullOutputPath))
            {
                try
                {
                    processingStepProgress?.Report(0);
                    await ReencodeVideoAsync(fullOutputPath, item.SelectedCodec, progressText, progressPercent, processingStepProgress, rawOutputProgress, token).ConfigureAwait(false);
                    processingStepProgress?.Report(100);
                }
                catch (OperationCanceledException)
                {
                    LogService.Log($"Rekoding avbrutt: {item.Title}", LogLevel.Info, _settings);
                    await Task.Delay(500).ConfigureAwait(false);
                    foreach (var file in Directory.GetFiles(tempPath, $"{cleanupBasePattern}*"))
                        try { File.Delete(file); } catch { }
                    throw;
                }
            }
            else
            {
                processingStepProgress?.Report(100);
                progressText.Report("Ferdigstiller fil...");
            }

            if (File.Exists(fullOutputPath))
            {
                string dest = Path.Combine(outputFolder, finalFileName);
                if (File.Exists(dest)) File.Delete(dest);

                bool moved = false;
                for (int i = 0; i < 5; i++)
                {
                    try { File.Move(fullOutputPath, dest); moved = true; break; }
                    catch { await Task.Delay(500).ConfigureAwait(false); }
                }

                if (moved)
                {
                    LogService.Log($"Ferdig! Lagret til: {dest}", LogLevel.Info, _settings);
                    downloadStepProgress?.Report(100);
                    processingStepProgress?.Report(100);
                    progressText.Report("Ferdig");
                    progressPercent.Report(100);
                    rawOutputProgress?.Report($"✓ Fullført: {finalFileName}");
                }
                else
                {
                    LogService.Log($"Kunne ikke flytte fil til {dest}. Fil last?", LogLevel.Error, _settings);
                    rawOutputProgress?.Report("✖ Feil: Kunne ikke flytte ferdig fil til lagringsmappe.");
                    throw new Exception("Fil last, kunne ikke flytte til output.");
                }
            }
            else
            {
                LogService.Log($"Fant ikke filen etter nedlasting: {fullOutputPath}", LogLevel.Error, _settings);
                rawOutputProgress?.Report($"✖ Feil: Fant ikke filen etter nedlasting ({fullOutputPath}).");
                throw new Exception("Fil ikke funnet etter nedlasting.");
            }
        }

        private const double DownloadShareWhenReencoding = 50;

        /// <summary>
        /// Rekoder første videostrøm med FFmpeg ved bruk av maskinvareakselerasjon (GPU) dersom tilgjengelig,
        /// med automatisk fallback til CPU ved feil. Lyd, undertekster og vedlegg kopieres uendret.
        /// Erstatter inputFile med resultatet når rekodingen lykkes.
        /// </summary>
        private async Task ReencodeVideoAsync(string inputFile, string codec, IProgress<string> progressText, IProgress<double> progressPercent, IProgress<double>? processingStepProgress, IProgress<string>? rawOutputProgress, CancellationToken token)
        {
            var encoderInfo = await HardwareEncoderService.GetEncoderAsync(codec, _settings.UseHardwareAcceleration, _ffmpegPath).ConfigureAwait(false);
            bool success = await TryRunFfmpegEncodeAsync(inputFile, codec, encoderInfo, progressText, progressPercent, processingStepProgress, rawOutputProgress, token).ConfigureAwait(false);

            if (!success && encoderInfo.IsHardwareAccelerated && !token.IsCancellationRequested)
            {
                var cpuEncoder = HardwareEncoderService.GetCpuEncoder(codec);
                LogService.Log($"GPU-koding feilet for {codec}. Bytter automatisk til CPU ({cpuEncoder.DisplayName})...", LogLevel.Info, _settings);
                rawOutputProgress?.Report($"⚠ GPU-rekoding feilet. Bytter automatisk til prosessor ({cpuEncoder.DisplayName})...");

                success = await TryRunFfmpegEncodeAsync(inputFile, codec, cpuEncoder, progressText, progressPercent, processingStepProgress, rawOutputProgress, token).ConfigureAwait(false);
            }

            if (!success && !token.IsCancellationRequested)
            {
                LogService.Log($"Rekoding feilet for {inputFile}. Beholder originalfilen.", LogLevel.Error, _settings);
                rawOutputProgress?.Report("⚠ Rekoding feilet. Beholder originalfilen uten omkoding.");
            }
        }

        private async Task<bool> TryRunFfmpegEncodeAsync(string inputFile, string codec, HardwareEncoderInfo encoderInfo, IProgress<string> progressText, IProgress<double> progressPercent, IProgress<double>? processingStepProgress, IProgress<string>? rawOutputProgress, CancellationToken token)
        {
            string codecName = VideoCodecs.ShortName(codec);
            string encodedFile = Path.Combine(
                Path.GetDirectoryName(inputFile) ?? "",
                $"{Path.GetFileNameWithoutExtension(inputFile)}.rekoding{Path.GetExtension(inputFile)}");

            try
            {
                if (File.Exists(encodedFile)) File.Delete(encodedFile);
            }
            catch { }

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            foreach (var arg in new[] { "-hide_banner", "-nostdin", "-y" })
                startInfo.ArgumentList.Add(arg);

            if (encoderInfo.DecoderArgs != null && encoderInfo.DecoderArgs.Length > 0)
            {
                foreach (var arg in encoderInfo.DecoderArgs)
                    startInfo.ArgumentList.Add(arg);
            }

            foreach (var arg in new[] { "-i", inputFile, "-map", "0", "-c", "copy" })
                startInfo.ArgumentList.Add(arg);

            foreach (var arg in encoderInfo.EncoderArgs)
                startInfo.ArgumentList.Add(arg);
            foreach (var arg in new[] { "-nostats", "-progress", "pipe:1", encodedFile })
                startInfo.ArgumentList.Add(arg);

            LogService.Log($"Starter rekoding: {encoderInfo.DisplayName} for {Path.GetFileName(inputFile)}", LogLevel.Info, _settings);
            LogService.Log($"Kommando: ffmpeg {string.Join(" ", startInfo.ArgumentList)}", LogLevel.Debug, _settings);

            string hwInfo = encoderInfo.IsHardwareAccelerated
                ? $"maskinvareakselerasjon ({encoderInfo.DisplayName})"
                : $"prosessor ({encoderInfo.DisplayName})";
            progressText.Report($"Koder til {codecName} ({encoderInfo.DisplayName})...");
            progressPercent.Report(DownloadShareWhenReencoding);
            rawOutputProgress?.Report($"• Rekoder til {codecName} med {hwInfo}...");

            var transcodeStopwatch = Stopwatch.StartNew();
            double durationSeconds = 0;
            int lastReportedPercent = -1;
            int lastReportedDecade = 0;
            string speed = "";
            string lastStableSpeed = "";
            var recentErrors = new Queue<string>(12);

            using (var process = new Process { StartInfo = startInfo })
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    if (durationSeconds <= 0 && e.Data.Contains("Duration:", StringComparison.Ordinal))
                    {
                        var durationMatch = FfmpegDurationRegex().Match(e.Data);
                        if (durationMatch.Success && TimeSpan.TryParse(durationMatch.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var duration))
                            durationSeconds = duration.TotalSeconds;
                    }

                    lock (recentErrors)
                    {
                        recentErrors.Enqueue(e.Data);
                        if (recentErrors.Count > 10) recentErrors.Dequeue();
                    }

                    if (_settings.LogLevel == LogLevel.Debug)
                    {
                        LogService.Log($"ffmpeg: {e.Data.Trim()}", LogLevel.Debug, _settings);
                    }
                };

                // Bruker ReadOnlySpan<char> for å unngå tusenvis av streng-allokeringer under rekoding
                process.OutputDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    ReadOnlySpan<char> lineSpan = e.Data.AsSpan();

                    if (lineSpan.StartsWith("speed=", StringComparison.Ordinal))
                    {
                        ReadOnlySpan<char> valSpan = lineSpan[6..].Trim();
                        if (!valSpan.IsEmpty && !valSpan.Equals("N/A", StringComparison.Ordinal))
                        {
                            speed = valSpan.ToString();
                            lastStableSpeed = speed;
                        }
                    }
                    else if (durationSeconds > 0 && lineSpan.StartsWith("out_time_us=", StringComparison.Ordinal))
                    {
                        ReadOnlySpan<char> valSpan = lineSpan[12..].Trim();
                        if (long.TryParse(valSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out long outTimeUs))
                        {
                            int encodePercent = (int)Math.Clamp(outTimeUs / 1_000_000.0 / durationSeconds * 100, 0, 100);
                            if (encodePercent == lastReportedPercent) return;
                            lastReportedPercent = encodePercent;

                            processingStepProgress?.Report(encodePercent);
                            double total = DownloadShareWhenReencoding + encodePercent * (99 - DownloadShareWhenReencoding) / 100.0;
                            progressPercent.Report(total);
                            string speedInfo = !string.IsNullOrEmpty(speed) ? $" {speed}" : "";
                            progressText.Report($"Koder til {codecName}... ({encodePercent}%){speedInfo}");

                            if (_settings.IsDebugDownloadLog)
                            {
                                int currentDecade = (encodePercent / 10) * 10;
                                if (currentDecade > lastReportedDecade && currentDecade < 100)
                                {
                                    lastReportedDecade = currentDecade;
                                    rawOutputProgress?.Report($"  ↳ Rekoding til {codecName}: {currentDecade}%{speedInfo}");
                                }
                            }
                        }
                    }
                };

                using (token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested)
                {
                    try { if (File.Exists(encodedFile)) File.Delete(encodedFile); } catch { }
                    throw new OperationCanceledException(token);
                }

                if (process.ExitCode != 0)
                {
                    try { if (File.Exists(encodedFile)) File.Delete(encodedFile); } catch { }
                    string details;
                    lock (recentErrors) details = string.Join(" | ", recentErrors);
                    LogService.Log($"Koding feilet med {encoderInfo.DisplayName} (kode {process.ExitCode}): {details}", LogLevel.Error, _settings);
                    return false;
                }
            }

            bool replaced = false;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(inputFile)) File.Delete(inputFile);
                    File.Move(encodedFile, inputFile);
                    replaced = true;
                    break;
                }
                catch when (i < 4)
                {
                    await Task.Delay(400).ConfigureAwait(false);
                }
            }

            if (!replaced)
            {
                LogService.Log("Kunne ikke erstatte originalfil med rekodet fil.", LogLevel.Error, _settings);
                return false;
            }

            transcodeStopwatch.Stop();
            string transcodeDuration = FormatDuration(transcodeStopwatch.Elapsed);
            string speedSummary = !string.IsNullOrEmpty(lastStableSpeed) ? $" ({lastStableSpeed} hastighet)" : "";
            rawOutputProgress?.Report($"  ↳ Transkodet til {codecName} på {transcodeDuration}{speedSummary} med {encoderInfo.DisplayName}");
            LogService.Log($"Rekoding ferdig: {Path.GetFileName(inputFile)} med {encoderInfo.DisplayName} på {transcodeDuration}{speedSummary}", LogLevel.Info, _settings);
            return true;
        }

        /// <summary>
        /// Skalerer 0-100 inn i et delintervall av totalfremdriften, uten ekstra dispatch.
        /// </summary>
        private sealed class ScaledProgress : IProgress<double>
        {
            private readonly IProgress<double> _inner;
            private readonly double _offset;
            private readonly double _share;

            public ScaledProgress(IProgress<double> inner, double offset, double share)
            {
                _inner = inner;
                _offset = offset;
                _share = share;
            }

            public void Report(double value) => _inner.Report(_offset + value * _share / 100.0);
        }

        internal void DetectMediaFile(string line)
        {
            // Sjekker direkte med OrdinalIgnoreCase uten å allokere en ny lowerLine-streng for hver eneste linje
            if (!line.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                return;

            if (line.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(".webp", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(".vtt", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(".srt", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(".xml", StringComparison.OrdinalIgnoreCase))
            {
                _isIgnoringCurrentFile = true;
                return;
            }

            _isIgnoringCurrentFile = false;
            _mediaFileCounter++;
            _lastLoggedPercent = -1;
        }

        internal void ParseProgress(string line, IProgress<string> text, IProgress<double> percent, IProgress<double>? downloadStepProgress = null, IProgress<string>? rawOutputProgress = null)
        {
            if (line.StartsWith("[download]", StringComparison.Ordinal))
            {
                var match = DownloadPercentRegex().Match(line);
                if (match.Success && double.TryParse(match.Groups[1].ValueSpan, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawPercent))
                {
                    if (_isIgnoringCurrentFile) return;

                    double calculatedPercent = _mediaFileCounter <= 1
                        ? rawPercent * 0.90
                        : 90 + (rawPercent * 0.10);

                    if (calculatedPercent < _maxReportedPercent) calculatedPercent = _maxReportedPercent;
                    else _maxReportedPercent = calculatedPercent;

                    if (calculatedPercent > 99) calculatedPercent = 99;

                    int currentWhole = (int)Math.Floor(calculatedPercent);
                    int lastWhole = _lastLoggedPercent >= 0 ? (int)Math.Floor(_lastLoggedPercent) : -1;

                    // Rapporterer kun til UI når heltallsprosenten faktisk endrer seg (sparer hundrevis av UI-kall/allokeringer)
                    if (currentWhole != lastWhole || rawPercent >= 100)
                    {
                        downloadStepProgress?.Report(calculatedPercent);
                        percent.Report(calculatedPercent);

                        var speedMatch = DownloadSpeedRegex().Match(line);
                        var etaMatch = DownloadEtaRegex().Match(line);
                        string speed = speedMatch.Success ? speedMatch.Groups[1].Value : "";
                        string eta = etaMatch.Success ? etaMatch.Groups[1].Value : "";

                        if (!string.IsNullOrEmpty(speed))
                        {
                            _lastObservedSpeed = speed;
                        }

                        string statusText = (!string.IsNullOrEmpty(speed), !string.IsNullOrEmpty(eta) && eta != "00:00") switch
                        {
                            (true, true) => $"Laster ned... ({currentWhole}%) ved {speed} [ETA {eta}]",
                            (true, false) => $"Laster ned... ({currentWhole}%) ved {speed}",
                            (false, true) => $"Laster ned... ({currentWhole}%) [ETA {eta}]",
                            _ => $"Laster ned... ({currentWhole}%)"
                        };

                        text.Report(statusText);

                        if (_settings.IsDebugDownloadLog)
                        {
                            int currentDecade = (currentWhole / 10) * 10;
                            if (currentDecade > _lastLoggedDecade && currentDecade > 0 && currentDecade <= 100)
                            {
                                _lastLoggedDecade = currentDecade;
                                string speedInfo = !string.IsNullOrEmpty(speed) ? $" ved {speed}" : "";
                                string etaInfo = !string.IsNullOrEmpty(eta) && eta != "00:00" ? $" (ETA {eta})" : "";
                                rawOutputProgress?.Report($"  ↳ Nedlasting: {currentDecade}%{speedInfo}{etaInfo}");
                            }
                        }

                        if (_settings.LogLevel == LogLevel.Debug)
                        {
                            LogService.Log($"yt-dlp: {line.Trim()}", LogLevel.Debug, _settings);
                        }

                        _lastLoggedPercent = calculatedPercent;
                    }
                    return;
                }
            }

            if (line.Contains("[Merger]", StringComparison.Ordinal) ||
                line.Contains("Merging formats", StringComparison.Ordinal) ||
                line.Contains("[VideoRemuxer]", StringComparison.Ordinal) ||
                line.Contains("[EmbedSubtitle]", StringComparison.Ordinal) ||
                line.Contains("[EmbedThumbnail]", StringComparison.Ordinal) ||
                line.Contains("Writing video", StringComparison.Ordinal))
            {
                if (!_hasLoggedMerger)
                {
                    _hasLoggedMerger = true;
                    downloadStepProgress?.Report(100);
                    text.Report("Klargjør video og undertekster...");
                    percent.Report(99);
                    if (_settings.IsDebugDownloadLog)
                    {
                        rawOutputProgress?.Report("• Klargjør video og undertekster...");
                    }
                }
                LogService.Log($"Ferdigstiller: {line.Trim()}", LogLevel.Debug, _settings);
                return;
            }

            if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                rawOutputProgress?.Report($"✖ Feil: {line.Trim()}");
                LogService.Log($"yt-dlp feil: {line.Trim()}", LogLevel.Error, _settings);
            }
            else if (_settings.LogLevel == LogLevel.Debug && !line.Contains("(frag ", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Log($"yt-dlp: {line.Trim()}", LogLevel.Debug, _settings);
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 1)
                return "< 1s";
            if (ts.TotalSeconds < 60)
                return $"{(int)Math.Round(ts.TotalSeconds)}s";
            if (ts.TotalHours < 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{(int)ts.TotalHours}t {ts.Minutes}m {ts.Seconds}s";
        }

        [GeneratedRegex(@"\[download\]\s+(\d+(\.\d+)?)%")]
        private static partial Regex DownloadPercentRegex();

        [GeneratedRegex(@"at\s+([^\s]+)")]
        private static partial Regex DownloadSpeedRegex();

        [GeneratedRegex(@"ETA\s+([^\s]+)")]
        private static partial Regex DownloadEtaRegex();

        [GeneratedRegex(@"Duration:\s*(\d+:\d{2}:\d{2}(\.\d+)?)")]
        private static partial Regex FfmpegDurationRegex();

        [GeneratedRegex(@"^[\s-–]+")]
        private static partial Regex LeadingSeparatorRegex();

        [GeneratedRegex(@"^\d+\.\s+")]
        private static partial Regex LeadingNumberRegex();

        [GeneratedRegex(@"([\\/:*?""<>|\x00-\x1F]*\.+$)|([\\/:*?""<>|\x00-\x1F]+)")]
        private static partial Regex SanitizeFileNameRegex();

        [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,2})")]
        private static partial Regex ShortSeasonEpisodeRegex();

        [GeneratedRegex(@"(?:Sesong|S)\s*(\d+).*?(?:Episode|E)\s*(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex LongSeasonEpisodeRegex();

        [GeneratedRegex(@"sesong[/-](\d+)[/-]episode[/-](\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex UrlSeasonEpisodeRegex();

        [GeneratedRegex(@"^(?:Episode\s*|E)(\d{1,3})", RegexOptions.IgnoreCase)]
        private static partial Regex EpisodePrefixRegex();

        [GeneratedRegex(@"^(\d{1,3})\.\s+")]
        private static partial Regex LeadingNumberCaptureRegex();
    }
}