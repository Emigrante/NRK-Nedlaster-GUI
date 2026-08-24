using System;

namespace NRKLastNed.Core.Models
{
    public class AppUpdateInfo
    {
        public bool IsNewVersionAvailable { get; set; }
        public string LatestVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
    }

    public class ToolUpdateInfo
    {
        public bool IsNewVersionAvailable { get; set; }
        public string LatestVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
    }

    public class FfmpegUpdateInfo
    {
        public bool IsNewVersionAvailable { get; set; }
        public string LatestVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ChecksumUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime PublishedAt { get; set; }
    }

    public class FfmpegUpdateResult
    {
        public bool Success { get; set; }
        public bool ChecksumVerified { get; set; }
        public string Sha256 { get; set; } = "";
        public string ExpectedSha256 { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
