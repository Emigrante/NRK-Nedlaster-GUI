using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NRKLastNed.Core.Contracts;

namespace NRKLastNed.Core.Services
{
    public class PlatformService : IPlatformService
    {
        public static PlatformService Instance { get; } = new PlatformService();

        public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public string GetExecutableExtension() => IsWindows ? ".exe" : "";

        public string GetToolBinaryName(string toolName) => toolName + GetExecutableExtension();

        public string GetToolPath(string toolName)
        {
            string toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            return Path.Combine(toolsPath, GetToolBinaryName(toolName));
        }

        public void OpenFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            try
            {
                if (IsMacOS)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "open",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(folderPath);
                    Process.Start(psi);
                }
                else if (IsWindows)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(folderPath);
                    Process.Start(psi);
                }
                else if (IsLinux)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(folderPath);
                    Process.Start(psi);
                }
            }
            catch { }
        }

        public void SetExecutablePermission(string filePath)
        {
            if (IsMacOS || IsLinux)
            {
                try
                {
                    var chmod = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    chmod.ArgumentList.Add("+x");
                    chmod.ArgumentList.Add(filePath);
                    Process.Start(chmod)?.WaitForExit();
                }
                catch { }
            }
        }

        public Task OpenUrlAsync(string url)
        {
            try
            {
                if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (IsMacOS)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "open",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(url);
                    Process.Start(psi);
                }
                else if (IsLinux)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(url);
                    Process.Start(psi);
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public string GetDefaultOutputFolder()
        {
            if (IsMacOS)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Movies", "NRK");
            }
            if (IsWindows)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NRK");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "NRK");
        }

        public string GetFFmpegDownloadFilename()
        {
            if (IsMacOS) return "macos-gpl.zip";
            if (IsWindows) return "win64-gpl.zip";
            return "linux-gpl.zip";
        }
    }
}
