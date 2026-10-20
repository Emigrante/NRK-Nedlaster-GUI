using System;
using System.Collections.Generic;
using System.IO;
using NRKLastNed.Core.Contracts;

namespace NRKLastNed.Core.Services
{
    public class ToolsStatus
    {
        public bool MissingFfmpeg { get; set; }
        public bool MissingFfprobe { get; set; }
        public bool MissingYtDlp { get; set; }
        public bool HasMissingTools => MissingFfmpeg || MissingFfprobe || MissingYtDlp;
        public List<string> MissingToolNames { get; set; } = new();
        public string ToolsDirectory { get; set; } = "";
    }

    public static class ToolsCheckService
    {
        public static ToolsStatus CheckTools(IPlatformService? platform = null)
        {
            platform ??= PlatformService.Instance;
            var status = new ToolsStatus
            {
                ToolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools")
            };

            string ffmpegPath = platform.GetToolPath("ffmpeg");
            string ffprobePath = platform.GetToolPath("ffprobe");
            string ytDlpPath = platform.GetToolPath("yt-dlp");

            if (!File.Exists(ffmpegPath))
            {
                status.MissingFfmpeg = true;
                status.MissingToolNames.Add(platform.GetToolBinaryName("ffmpeg"));
            }

            if (!File.Exists(ffprobePath))
            {
                status.MissingFfprobe = true;
                status.MissingToolNames.Add(platform.GetToolBinaryName("ffprobe"));
            }

            if (!File.Exists(ytDlpPath))
            {
                status.MissingYtDlp = true;
                status.MissingToolNames.Add(platform.GetToolBinaryName("yt-dlp"));
            }

            return status;
        }
    }
}
