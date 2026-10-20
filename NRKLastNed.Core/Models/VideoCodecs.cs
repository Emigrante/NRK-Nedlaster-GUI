using System;
using System.Collections.Generic;

namespace NRKLastNed.Core.Models
{
    /// <summary>
    /// Videokodeks brukeren kan velge for sluttfilen.
    /// NRK sender allerede video i H.264 (x264), så x264 krever ingen rekoding (Original).
    /// x265 (HEVC) og AV1 rekodes via GPU/maskinvareakselerasjon eller CPU fallback.
    /// </summary>
    public static class VideoCodecs
    {
        public const string H264 = "x264 (Original)";
        public const string Hevc = "x265 (HEVC)";
        public const string Av1 = "AV1";

        // Bakoverkompatibilitet for lagrede innstillinger
        public const string LegacyH264 = "x264 (H.264)";
        public const string None = "Ingen rekoding";
        public const string LegacyNone = "Ingen rekoding (Original)";

        public static IReadOnlyList<string> All { get; } = new[] { H264, Hevc, Av1 };

        public static string ShortName(string codec) => codec switch
        {
            H264 or LegacyH264 or None or LegacyNone => "x264",
            Hevc => "x265",
            Av1 => "AV1",
            _ => codec
        };

        /// <summary>
        /// Returnerer kodek-tag for filnavn (f.eks. "x265", "x264", "AV1").
        /// NRK sin standardstrøm er H.264, så fallback er "x264".
        /// </summary>
        public static string GetSceneCodecTag(string? codec)
        {
            if (string.IsNullOrWhiteSpace(codec)) return "x264";
            if (codec.Contains("265", StringComparison.OrdinalIgnoreCase) || codec.Contains("hevc", StringComparison.OrdinalIgnoreCase)) return "x265";
            if (codec.Contains("av1", StringComparison.OrdinalIgnoreCase)) return "AV1";
            if (codec.Contains("264", StringComparison.OrdinalIgnoreCase) || codec.Contains("avc", StringComparison.OrdinalIgnoreCase)) return "x264";
            return "x264";
        }

        /// <summary>
        /// Kun x265 og AV1 krever rekoding. x264 er originalstrømmen fra NRK og beholdes uendret.
        /// </summary>
        public static bool IsReencoding(string? codec)
        {
            if (string.IsNullOrWhiteSpace(codec)) return false;
            if (codec == H264 || codec == LegacyH264 || codec == None || codec == LegacyNone || codec.Contains("264"))
                return false;

            return codec == Hevc || codec == Av1 || codec.Contains("265") || codec.Contains("av1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
