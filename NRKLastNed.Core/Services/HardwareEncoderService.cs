using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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

                var features = new List<string>(3);
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
        private static readonly SemaphoreSlim _detectionLock = new(1, 1);

        /// <summary>
        /// Finner GPU og verifiserer tilgjengelige maskinvareenkodere i FFmpeg.
        /// Kjører testene parallelt og cacher resultatet i minnet for hele appens levetid.
        /// </summary>
        public static async Task<HardwareCapabilities> GetCapabilitiesAsync(string ffmpegPath)
        {
            if (_cachedCapabilities != null)
                return _cachedCapabilities;

            await _detectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cachedCapabilities != null)
                    return _cachedCapabilities;

                var caps = new HardwareCapabilities();

                try
                {
                    string gpuName = await DetectGpuNameAsync().ConfigureAwait(false);
                    caps.GpuName = string.IsNullOrWhiteSpace(gpuName) ? "Standard GPU" : gpuName;
                    caps.Vendor = DetectVendor(caps.GpuName);

                    if (File.Exists(ffmpegPath) && caps.Vendor != GpuVendor.None && caps.Vendor != GpuVendor.Unknown)
                    {
                        // Kjør alle tre kodeks-testene parallelt i stedet for sekvensielt (3x raskere!)
                        var av1Task = ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "AV1"));
                        var hevcTask = ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "HEVC"));
                        var h264Task = ProbeEncoderAsync(ffmpegPath, GetCandidateEncoder(caps.Vendor, "H264"));

                        await Task.WhenAll(av1Task, hevcTask, h264Task).ConfigureAwait(false);

                        caps.SupportsAv1 = await av1Task.ConfigureAwait(false);
                        caps.SupportsHevc = await hevcTask.ConfigureAwait(false);
                        caps.SupportsH264 = await h264Task.ConfigureAwait(false);
                    }
                }
                catch
                {
                    caps.Vendor = GpuVendor.None;
                }

                _cachedCapabilities = caps;
                LogService.Log($"Maskinvare-deteksjon: GPU='{caps.GpuName}', Produsent={caps.Vendor}, AV1={caps.SupportsAv1}, HEVC={caps.SupportsHevc}, H264={caps.SupportsH264}", LogLevel.Info);
                return caps;
            }
            finally
            {
                _detectionLock.Release();
            }
        }

        public static HardwareCapabilities GetCachedCapabilities()
        {
            return _cachedCapabilities ?? new HardwareCapabilities { GpuName = "Sjekker...", Vendor = GpuVendor.Unknown };
        }

        /// <summary>
        /// Asynkron metode for å hente enkoder uten å blokkere tråden.
        /// </summary>
        public static async Task<HardwareEncoderInfo> GetEncoderAsync(string codec, bool useHardware, string ffmpegPath)
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
                    caps = await GetCapabilitiesAsync(ffmpegPath).ConfigureAwait(false);
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

            var cpu = GetCpuEncoder(codec);
            LogService.Log($"Valgt CPU-enkoder for {codec}: {cpu.DisplayName} (useHardware={useHardware}, Vendor={caps.Vendor}, SupportsAv1={caps.SupportsAv1})", LogLevel.Info);
            return cpu;
        }

        /// <summary>
        /// Synkron wrapper for bakoverkompatibilitet.
        /// </summary>
        public static HardwareEncoderInfo GetEncoder(string codec, bool useHardware, string ffmpegPath)
        {
            return GetEncoderAsync(codec, useHardware, ffmpegPath).GetAwaiter().GetResult();
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

            if (gpuName.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("quadro", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("rtx", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("gtx", StringComparison.OrdinalIgnoreCase))
                return GpuVendor.Nvidia;

            if (gpuName.Contains("intel", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("arc", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("iris", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("uhd", StringComparison.OrdinalIgnoreCase))
                return GpuVendor.Intel;

            if (gpuName.Contains("amd", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("radeon", StringComparison.OrdinalIgnoreCase))
                return GpuVendor.Amd;

            return GpuVendor.Unknown;
        }

        private static async Task<string> DetectGpuNameAsync()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // 1. Lynrask sjekk med reg.exe (~15 ms og minimalt minne sammenlignet med ~2.5s for powershell.exe)
                    var regPsi = new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        ArgumentList =
                        {
                            "query",
                            @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
                            "/s",
                            "/v",
                            "DriverDesc"
                        },
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var candidates = new List<string>();
                    using (var regProc = Process.Start(regPsi))
                    {
                        if (regProc != null)
                        {
                            string regOutput = await regProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                            await regProc.WaitForExitAsync().ConfigureAwait(false);

                            var lines = regOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                int idx = line.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    string desc = line[(idx + 6)..].Trim();
                                    if (!string.IsNullOrWhiteSpace(desc))
                                    {
                                        candidates.Add(desc);
                                    }
                                }
                            }
                        }
                    }

                    string? best = SelectBestGpuCandidate(candidates);
                    if (!string.IsNullOrEmpty(best))
                        return best;

                    // 2. Fallback til PowerShell dersom registeroppslag ikke ga resultat
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
                        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                        var psLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        best = SelectBestGpuCandidate(psLines);
                        if (!string.IsNullOrEmpty(best))
                            return best;
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

        private static string? SelectBestGpuCandidate(IEnumerable<string> names)
        {
            string? fallback = null;
            foreach (var raw in names)
            {
                string name = raw.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                // Prioriter dedikerte kort (Intel Arc, NVIDIA RTX/GTX, AMD Radeon)
                if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }

                if (DetectVendor(name) != GpuVendor.Unknown && fallback == null)
                {
                    fallback = name;
                }
            }
            return fallback;
        }

        /// <summary>
        /// Tester om en enkoder faktisk lar seg initialisere av den lokale FFmpeg-binæren.
        /// Kjører uten unødvendig stdout/stderr-buffering i minnet.
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
                        "-loglevel", "quiet",
                        "-nostdin",
                        "-f", "lavfi",
                        "-i", "nullsrc=s=256x256:d=0.1",
                        "-frames:v", "1",
                        "-c:v", encoder,
                        "-f", "null",
                        "-"
                    },
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                using var cts = new CancellationTokenSource(4000);
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}