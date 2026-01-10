using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NRKLastNed.Mac
{
    public static class PlatformHelper
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public static string GetExecutableExtension()
        {
            return IsWindows ? ".exe" : "";
        }

        public static string GetToolPath(string toolName)
        {
            string toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            return Path.Combine(toolsPath, toolName + GetExecutableExtension());
        }

        public static void OpenFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            try
            {
                if (IsMacOS)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else if (IsLinux)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception)
            {
                // Ignore errors
            }
        }

        public static string GetDefaultOutputFolder()
        {
            if (IsMacOS)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Movies", "NRK");
            }
            else if (IsWindows)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NRK");
            }
            else
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "NRK");
            }
        }

        public static string GetFFmpegDownloadFilename()
        {
            if (IsMacOS)
            {
                return "macos-gpl.zip";
            }
            else if (IsWindows)
            {
                return "win64-gpl.zip";
            }
            else
            {
                return "linux-gpl.zip";
            }
        }

        public static string GetToolBinaryName(string toolName)
        {
            // On macOS and Linux, tools don't have .exe extension
            // ffmpeg/ffprobe are executable, yt-dlp needs to be executable
            return toolName + GetExecutableExtension();
        }
    }
}
