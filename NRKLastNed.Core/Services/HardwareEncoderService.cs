using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Core.Services
{
    public enum GpuVendor
    {
        Unknown,
        Intel,
        Nvidia,
        Amd,
        Apple,
        None
    }

    public class HardwareEncoderInfo
    {
        public string EncoderName { get; set; } = "";
        public string[] DecoderArgs { get; set; } = Array.Empty<string>();
        public string[] EncoderArgs { get; set; } = Array.Empty<string>();
        public string DisplayName { get; set; } = "";
        public bool IsHardwareAccelerated { get; set; }
    }

    public class HardwareCapabilities
    {
        public string GpuName { get; set; } = "Ingen oppdaget";
        public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
        public bool SupportsAv1 { get; set; }
        public bool SupportsHevc { get; set; }
        public bool SupportsH264 { get; set; }

        public string Description
        {
            get
            {
                if (Vendor == GpuVendor.None || Vendor == GpuVendor.Unknown)
                    return "Ingen kompatibel GPU funnet (bruker CPU)";

                var features = new System.Collections.Generic.List<string>();
                if (SupportsAv1) features.Add("AV1");
                if (SupportsHevc) features.Add("HEVC");
                if (SupportsH264) features.Add("H.264");

                string vendorTag = Vendor switch
                {
                    GpuVendor.Intel => "Intel QSV",
                    GpuVendor.Nvidia => "NVIDIA NVENC",
                    GpuVendor.Amd => "AMD AMF",
                    GpuVendor.Apple => "Apple VideoToolbox",
                    _ => "GPU"
                };

                return $"{GpuName} ({vendorTag}: {string.Join(", ", features)})";
            }
        }
    }

    public static class HardwareEncoderService
    {
        private static HardwareCapabilities? _cachedCapabilities;
        private static readonly object _lock = new();

        /// <summary>
        /// Finner GPU og verifiserer tilgjengelige maskinvareenkodere i FFmpeg.
        /// Resultatet caches i minnet for appens levetid.
        /// </summary>
        public static async Task<HardwareCapabilities> GetCapabilitiesAsync(string ffmpegPath)
        {
            if (_cachedCapabilities != null)
                return _cachedCapabilities;

            return await Task.Run(async () =>
            {
                lock (_lock)
                {
                    if (_cachedCapabilities != null) return _cachedCapabilities;
                }

                var caps = new HardwareCapabilities();

                try
                {
                    string gpuName = await DetectGpuNameAsync();
                    caps.GpuName = string.IsNullOrWhiteSpace(gpuName) ? "Standard GPU" : gpuName;
                    caps.Vendor = DetectVendor(caps.GpuName);

                    if (File.Exists(ffmpegPath))
                    {
                        caps.SupportsAv1 = await ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "AV1"));
                        caps.SupportsHevc = await ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "HEVC"));
                        caps.SupportsH264 = await ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "H264"));
                    }
                }
                catch
                {
                    caps.Vendor = GpuVendor.None;
                }

                lock (_lock)
                {
                    _cachedCapabilities = caps;
                }

                LogService.Log($"Maskinvare-deteksjon: GPU='{caps.GpuName}', Produsent={caps.Vendor}, AV1={caps.SupportsAv1}, HEVC={caps.SupportsHevc}, H264={caps.SupportsH264}", LogLevel.Info);
                return caps;
            });
        }

        public static HardwareCapabilities GetCachedCapabilities()
        {
            return _cachedCapabilities ?? new HardwareCapabilities { GpuName = "Sjekker...", Vendor = GpuVendor.Unknown };
        }

        /// <summary>
        /// Returnerer optimal enkoder og argumenter for angitt kodeks.
        /// Hvis useHardware er true og GPU støtter kodeksen, velges GPU-enkoder.
        /// Ellers returneres en optimalisert CPU-enkoder.
        /// </summary>
        public static HardwareEncoderInfo GetEncoder(string codec, bool useHardware, string ffmpegPath)
        {
            if (codec == VideoCodecs.None || codec == VideoCodecs.LegacyNone)
            {
                return new HardwareEncoderInfo
                {
                    EncoderName = "copy",
                    DisplayName = "Ingen rekoding",
                    IsHardwareAccelerated = false
                };
            }

            var caps = _cachedCapabilities;
            if (caps == null && File.Exists(ffmpegPath))
            {
                try
                {
                    caps = GetCapabilitiesAsync(ffmpegPath).GetAwaiter().GetResult();
                }
                catch
                {
                    caps = new HardwareCapabilities { Vendor = GpuVendor.None };
                }
            }

            caps ??= new HardwareCapabilities { Vendor = GpuVendor.None };

            if (useHardware && caps.Vendor != GpuVendor.None && caps.Vendor != GpuVendor.Unknown)
            {
                if (codec == VideoCodecs.Av1 && caps.SupportsAv1)
                {
                    var enc = CreateGpuEncoder(caps.Vendor, "AV1", caps.GpuName);
                    LogService.Log($"Valgt GPU-enkoder for AV1: {enc.DisplayName}", LogLevel.Info);
                    return enc;
                }
                if (codec == VideoCodecs.Hevc && caps.SupportsHevc)
                {
                    var enc = CreateGpuEncoder(caps.Vendor, "HEVC", caps.GpuName);
                    LogService.Log($"Valgt GPU-enkoder for HEVC: {enc.DisplayName}", LogLevel.Info);
                    return enc;
                }
                if (codec == VideoCodecs.H264 && caps.SupportsH264)
                {
                    var enc = CreateGpuEncoder(caps.Vendor, "H264", caps.GpuName);
                    LogService.Log($"Valgt GPU-enkoder for H.264: {enc.DisplayName}", LogLevel.Info);
                    return enc;
                }
            }

            // Fallback til optimalisert CPU-enkoding
            var cpu = GetCpuEncoder(codec);
            LogService.Log($"Valgt CPU-enkoder for {codec}: {cpu.DisplayName} (useHardware={useHardware}, Vendor={caps.Vendor}, SupportsAv1={caps.SupportsAv1})", LogLevel.Info);
            return cpu;
        }

        public static HardwareEncoderInfo GetCpuEncoder(string codec)
        {
            return codec switch
            {
                VideoCodecs.Av1 => new HardwareEncoderInfo
                {
                    EncoderName = "libsvtav1",
                    EncoderArgs = new[] { "-c:v:0", "libsvtav1", "-preset", "10", "-crf", "30", "-pix_fmt", "yuv420p10le", "-threads", "0" },
                    DisplayName = "CPU (libsvtav1, preset 10, flertrådet)",
                    IsHardwareAccelerated = false
                },
                VideoCodecs.Hevc => new HardwareEncoderInfo
                {
                    EncoderName = "libx265",
                    EncoderArgs = new[] { "-c:v:0", "libx265", "-preset", "faster", "-crf", "24", "-pix_fmt", "yuv420p", "-threads", "0" },
                    DisplayName = "CPU (libx265, faster, flertrådet)",
                    IsHardwareAccelerated = false
                },
                _ => new HardwareEncoderInfo
                {
                    EncoderName = "libx264",
                    EncoderArgs = new[] { "-c:v:0", "libx264", "-preset", "veryfast", "-crf", "21", "-pix_fmt", "yuv420p", "-threads", "0" },
                    DisplayName = "CPU (libx264, veryfast, flertrådet)",
                    IsHardwareAccelerated = false
                }
            };
        }

        private static HardwareEncoderInfo CreateGpuEncoder(GpuVendor vendor, string type, string gpuName)
        {
            return (vendor, type) switch
            {
                // Intel Quick Sync Video (QSV) med maskinvaredekoding og asynkron pipeline
                (GpuVendor.Intel, "AV1") => new HardwareEncoderInfo
                {
                    EncoderName = "av1_qsv",
                    DecoderArgs = new[] { "-hwaccel", "qsv", "-hwaccel_output_format", "qsv" },
                    EncoderArgs = new[] { "-c:v:0", "av1_qsv", "-preset", "faster", "-global_quality", "28", "-async_depth", "8" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "Intel Arc GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Intel, "HEVC") => new HardwareEncoderInfo
                {
                    EncoderName = "hevc_qsv",
                    DecoderArgs = new[] { "-hwaccel", "qsv", "-hwaccel_output_format", "qsv" },
                    EncoderArgs = new[] { "-c:v:0", "hevc_qsv", "-preset", "faster", "-global_quality", "25", "-async_depth", "8" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "Intel GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Intel, "H264") => new HardwareEncoderInfo
                {
                    EncoderName = "h264_qsv",
                    DecoderArgs = new[] { "-hwaccel", "qsv", "-hwaccel_output_format", "qsv" },
                    EncoderArgs = new[] { "-c:v:0", "h264_qsv", "-preset", "faster", "-global_quality", "21", "-async_depth", "8" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "Intel GPU",
                    IsHardwareAccelerated = true
                },

                // NVIDIA NVENC med CUDA-dekoding
                (GpuVendor.Nvidia, "AV1") => new HardwareEncoderInfo
                {
                    EncoderName = "av1_nvenc",
                    DecoderArgs = new[] { "-hwaccel", "cuda" },
                    EncoderArgs = new[] { "-c:v:0", "av1_nvenc", "-preset", "p3", "-cq", "28" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "NVIDIA GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Nvidia, "HEVC") => new HardwareEncoderInfo
                {
                    EncoderName = "hevc_nvenc",
                    DecoderArgs = new[] { "-hwaccel", "cuda" },
                    EncoderArgs = new[] { "-c:v:0", "hevc_nvenc", "-preset", "p3", "-cq", "24", "-pix_fmt", "yuv420p" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "NVIDIA GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Nvidia, "H264") => new HardwareEncoderInfo
                {
                    EncoderName = "h264_nvenc",
                    DecoderArgs = new[] { "-hwaccel", "cuda" },
                    EncoderArgs = new[] { "-c:v:0", "h264_nvenc", "-preset", "p3", "-cq", "21", "-pix_fmt", "yuv420p" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "NVIDIA GPU",
                    IsHardwareAccelerated = true
                },

                // AMD AMF med D3D11VA-dekoding
                (GpuVendor.Amd, "AV1") => new HardwareEncoderInfo
                {
                    EncoderName = "av1_amf",
                    DecoderArgs = new[] { "-hwaccel", "d3d11va" },
                    EncoderArgs = new[] { "-c:v:0", "av1_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "28", "-qp_p", "28" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "AMD GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Amd, "HEVC") => new HardwareEncoderInfo
                {
                    EncoderName = "hevc_amf",
                    DecoderArgs = new[] { "-hwaccel", "d3d11va" },
                    EncoderArgs = new[] { "-c:v:0", "hevc_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "24", "-qp_p", "24" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "AMD GPU",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Amd, "H264") => new HardwareEncoderInfo
                {
                    EncoderName = "h264_amf",
                    DecoderArgs = new[] { "-hwaccel", "d3d11va" },
                    EncoderArgs = new[] { "-c:v:0", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "21", "-qp_p", "21" },
                    DisplayName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "AMD GPU",
                    IsHardwareAccelerated = true
                },

                // Apple VideoToolbox (macOS)
                (GpuVendor.Apple, "HEVC") => new HardwareEncoderInfo
                {
                    EncoderName = "hevc_videotoolbox",
                    DecoderArgs = new[] { "-hwaccel", "videotoolbox" },
                    EncoderArgs = new[] { "-c:v:0", "hevc_videotoolbox", "-q:v", "60", "-pix_fmt", "yuv420p" },
                    DisplayName = "Apple Silicon GPU (hevc_videotoolbox)",
                    IsHardwareAccelerated = true
                },
                (GpuVendor.Apple, "H264") => new HardwareEncoderInfo
                {
                    EncoderName = "h264_videotoolbox",
                    DecoderArgs = new[] { "-hwaccel", "videotoolbox" },
                    EncoderArgs = new[] { "-c:v:0", "h264_videotoolbox", "-q:v", "60", "-pix_fmt", "yuv420p" },
                    DisplayName = "Apple Silicon GPU (h264_videotoolbox)",
                    IsHardwareAccelerated = true
                },

                _ => GetCpuEncoder(type)
            };
        }

        private static string GetCandidateEncoder(GpuVendor vendor, string type) => (vendor, type) switch
        {
            (GpuVendor.Intel, "AV1") => "av1_qsv",
            (GpuVendor.Intel, "HEVC") => "hevc_qsv",
            (GpuVendor.Intel, "H264") => "h264_qsv",

            (GpuVendor.Nvidia, "AV1") => "av1_nvenc",
            (GpuVendor.Nvidia, "HEVC") => "hevc_nvenc",
            (GpuVendor.Nvidia, "H264") => "h264_nvenc",

            (GpuVendor.Amd, "AV1") => "av1_amf",
            (GpuVendor.Amd, "HEVC") => "hevc_amf",
            (GpuVendor.Amd, "H264") => "h264_amf",

            (GpuVendor.Apple, "HEVC") => "hevc_videotoolbox",
            (GpuVendor.Apple, "H264") => "h264_videotoolbox",

            _ => ""
        };

        private static GpuVendor DetectVendor(string gpuName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GpuVendor.Apple;

            string name = gpuName.ToLowerInvariant();
            if (name.Contains("nvidia") || name.Contains("geforce") || name.Contains("quadro") || name.Contains("rtx") || name.Contains("gtx"))
                return GpuVendor.Nvidia;
            if (name.Contains("intel") || name.Contains("arc") || name.Contains("iris") || name.Contains("uhd"))
                return GpuVendor.Intel;
            if (name.Contains("amd") || name.Contains("radeon"))
                return GpuVendor.Amd;

            return GpuVendor.Unknown;
        }

        private static async Task<string> DetectGpuNameAsync()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name" },
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0)
                            return lines[0].Trim();
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "Apple Silicon GPU";
                }
            }
            catch
            {
            }

            return "Standard GPU";
        }

        /// <summary>
        /// Tester om en enkoder faktisk lar seg initialisere av den lokale FFmpeg-binæren.
        /// Kjører en 0.1s dummytest med 256x256 oppløsning og 3 sekunder timeout.
        /// </summary>
        private static async Task<bool> ProbeEncoderAsync(string ffmpegPath, string encoder)
        {
            if (string.IsNullOrEmpty(encoder) || !File.Exists(ffmpegPath))
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    ArgumentList =
                    {
                        "-hide_banner",
                        "-f", "lavfi",
                        "-i", "nullsrc=s=256x256:d=0.1",
                        "-c:v", encoder,
                        "-f", "null",
                        "-"
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                using var cts = new System.Threading.CancellationTokenSource(10000);
                await process.WaitForExitAsync(cts.Token);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
