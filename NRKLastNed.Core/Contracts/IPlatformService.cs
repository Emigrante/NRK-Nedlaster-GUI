using System;
using System.Threading.Tasks;

namespace NRKLastNed.Core.Contracts
{
    public interface IPlatformService
    {
        bool IsWindows { get; }
        bool IsMacOS { get; }
        bool IsLinux { get; }
        string GetExecutableExtension();
        string GetToolPath(string toolName);
        string GetToolBinaryName(string toolName);
        string GetDefaultOutputFolder();
        string GetFFmpegDownloadFilename();
        void OpenFolder(string folderPath);
        void SetExecutablePermission(string filePath);
        Task OpenUrlAsync(string url);
    }
}
