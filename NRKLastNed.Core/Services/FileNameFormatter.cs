using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public static partial class FileNameFormatter
    {
        public const string Suffix = "-NRKNedlaster";

        /// <summary>
        /// Bygger scene-filnavn iht. spesifikasjon:
        /// TV-serie: &lt;serienavn&gt;.&lt;episode/sesong&gt;.&lt;oppløsning&gt;.WEB.&lt;kodek&gt;-NRKNedlaster.mkv
        /// Film/enkeltprogram: &lt;Film.navn&gt;.&lt;oppløsning&gt;.&lt;kodek&gt;-NRKNedlaster.mkv
        /// Radio: &lt;Programnavn&gt;.&lt;Episodenavn&gt;-NRKNedlaster.mp3 (eller &lt;Programnavn&gt;-NRKNedlaster.mp3)
        /// </summary>
        public static string BuildOutputFileName(DownloadItem item)
        {
            if (item == null) return $"Nedlasting{Suffix}.mkv";

            // Radio / Podkast: Alltid .mp3, ingen oppløsning eller videokodek
            if (!item.IsTelevision)
            {
                string progRaw = !string.IsNullOrWhiteSpace(item.SeriesTitle)
                    ? item.SeriesTitle
                    : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : "Radio");

                string prog = ToSceneDotNotation(progRaw);
                string ep = ToSceneDotNotation(item.EpisodeTitle);

                if (string.IsNullOrEmpty(prog)) prog = "Radio";

                if (!string.IsNullOrEmpty(ep) && !string.Equals(prog, ep, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{prog}.{ep}{Suffix}.mp3";
                }

                return $"{prog}{Suffix}.mp3";
            }

            // TV / Video: Alltid .mkv
            string res = GetSceneResolutionTag(item);
            string codec = VideoCodecs.GetSceneCodecTag(item.SelectedCodec);

            // TV-serie med sesong/episode: <serienavn>.<episode/sesong>.<oppløsning>.WEB.<kodek>-NRKNedlaster.mkv
            if (!string.IsNullOrWhiteSpace(item.SeasonEpisode))
            {
                string seriesRaw = !string.IsNullOrWhiteSpace(item.SeriesTitle)
                    ? item.SeriesTitle
                    : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : "TV");

                string series = ToSceneDotNotation(seriesRaw);
                string se = ToSceneDotNotation(item.SeasonEpisode);

                if (string.IsNullOrEmpty(series)) series = "TV";

                return $"{series}.{se}.{res}.WEB.{codec}{Suffix}.mkv";
            }

            // Film / Enkeltprogram uten sesong/episode: <Film.navn>.<oppløsning>.<kodek>-NRKNedlaster.mkv
            string filmRaw = !string.IsNullOrWhiteSpace(item.SeriesTitle)
                ? item.SeriesTitle
                : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : "Video");

            string film = ToSceneDotNotation(filmRaw);
            if (string.IsNullOrEmpty(film)) film = "Video";

            string epStandalone = ToSceneDotNotation(item.EpisodeTitle);
            if (!string.IsNullOrEmpty(epStandalone) && !string.Equals(film, epStandalone, StringComparison.OrdinalIgnoreCase))
            {
                return $"{film}.{epStandalone}.{res}.{codec}{Suffix}.mkv";
            }

            return $"{film}.{res}.{codec}{Suffix}.mkv";
        }

        public static string GetSceneResolutionTag(DownloadItem item)
        {
            string res = item.SelectedResolution ?? "";
            if (string.IsNullOrWhiteSpace(res) || string.Equals(res, "best", StringComparison.OrdinalIgnoreCase))
            {
                string? highest = item.AvailableResolutions?
                    .Where(r => int.TryParse(r, out _))
                    .OrderByDescending(r => int.Parse(r))
                    .FirstOrDefault();

                res = highest ?? "1080";
            }

            string digitsOnly = new string(res.Where(char.IsDigit).ToArray());
            if (int.TryParse(digitsOnly, out int numericRes) && numericRes > 0)
            {
                return $"{numericRes}p";
            }

            return "1080p";
        }

        public static string ToSceneDotNotation(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            string s = input;

            // Fjern ugyldige tegn for filsystem på tvers av plattformer
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, ' ');
            }

            // Fjern tegn som sitattegn, parenteser, klammer, tegnsetting
            s = s.Replace("\"", " ")
                 .Replace("'", "")
                 .Replace("`", "")
                 .Replace("«", " ")
                 .Replace("»", " ")
                 .Replace("“", " ")
                 .Replace("”", " ")
                 .Replace("(", " ")
                 .Replace(")", " ")
                 .Replace("[", " ")
                 .Replace("]", " ")
                 .Replace("{", " ")
                 .Replace("}", " ")
                 .Replace("!", "")
                 .Replace("?", "")
                 .Replace("&", " og ")
                 .Replace("+", " ")
                 .Replace(":", " ")
                 .Replace(";", " ")
                 .Replace(",", " ");

            // Konverter bindestrek omgitt av mellomrom eller vanlige mellomrom/understreker til punktum
            s = DashSeparatorRegex().Replace(s, ".");
            s = WhitespaceOrUnderscoreRegex().Replace(s, ".");

            // Slå sammen multiple punktum til ett
            s = MultipleDotsRegex().Replace(s, ".");

            return s.Trim('.');
        }

        [GeneratedRegex(@"\s*[-–—]\s*")]
        private static partial Regex DashSeparatorRegex();

        [GeneratedRegex(@"[\s_]+")]
        private static partial Regex WhitespaceOrUnderscoreRegex();

        [GeneratedRegex(@"\.+")]
        private static partial Regex MultipleDotsRegex();
    }
}
