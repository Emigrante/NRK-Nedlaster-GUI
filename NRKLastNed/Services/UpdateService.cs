using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NRKLastNed.Services
{
    public class UpdateService
    {
        private readonly string _toolsPath;
        private readonly string _ytDlpPath;

        public UpdateService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");
        }

        public async Task<string> GetYtDlpVersionAsync()
        {
            if (!File.Exists(_ytDlpPath)) return "Ikke installert";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return output.Trim();
                }
            }
            catch
            {
                return "Ukjent";
            }
        }

        public async Task<string> UpdateYtDlpAsync()
        {
            if (!File.Exists(_ytDlpPath)) return "Finner ikke filen.";

            // Kjører yt-dlp --update
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "--update",
                RedirectStandardOutput = true,
                RedirectStandardError = true, // Feilmeldinger havner ofte her
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    // Slå sammen output for å se hva som skjedde
                    return output + Environment.NewLine + error;
                }
            }
            catch (Exception ex)
            {
                return $"Feil under oppdatering: {ex.Message}";
            }
        }
    }
}