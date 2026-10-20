using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Services;

namespace NRKLastNed.Views
{
    public class MissingToolDisplayItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public partial class MissingToolsWindow : Window
    {
        private readonly ToolsStatus _status;
        private readonly IPlatformService _platform;
        private bool _isDownloading = false;
        private bool _installedSuccessfully = false;
        private bool _hasWarnedManual = false;

        public bool InstalledSuccessfully => _installedSuccessfully;

        public MissingToolsWindow(ToolsStatus status, IPlatformService? platform = null)
        {
            InitializeComponent();
            _status = status;
            _platform = platform ?? PlatformService.Instance;

            PopulateMissingToolsList();
        }

        private void PopulateMissingToolsList()
        {
            var items = new List<MissingToolDisplayItem>();

            if (_status.MissingYtDlp)
            {
                items.Add(new MissingToolDisplayItem
                {
                    Name = _platform.GetToolBinaryName("yt-dlp"),
                    Description = "Henter og laster ned video- og radiostrømmer fra NRK."
                });
            }

            if (_status.MissingFfmpeg)
            {
                items.Add(new MissingToolDisplayItem
                {
                    Name = _platform.GetToolBinaryName("ffmpeg"),
                    Description = "Hovedmotor for sammensetting av video/lyd, undertekster og omkoding."
                });
            }

            if (_status.MissingFfprobe)
            {
                items.Add(new MissingToolDisplayItem
                {
                    Name = _platform.GetToolBinaryName("ffprobe"),
                    Description = "Analyseverktøy for mediestrømmer, varighet og formater."
                });
            }

            icMissingTools.ItemsSource = items;
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = true;
            pnlActionButtons.Visibility = Visibility.Collapsed;
            pnlProgress.Visibility = Visibility.Visible;
            pbProgress.IsIndeterminate = true;

            try
            {
                // 1. Last ned yt-dlp dersom den mangler
                if (_status.MissingYtDlp)
                {
                    txtProgress.Text = "Laster ned yt-dlp fra GitHub releases...";
                    var updateService = new UpdateService(_platform);
                    var ytInfo = await updateService.CheckForYtDlpUpdateAsync();
                    string ytResult = await updateService.UpdateYtDlpAsync(ytInfo);
                    LogService.Log($"Automatisk installasjon av yt-dlp: {ytResult}", Core.Models.LogLevel.Info);
                }

                // 2. Last ned FFmpeg og FFprobe dersom en av dem mangler
                if (_status.MissingFfmpeg || _status.MissingFfprobe)
                {
                    txtProgress.Text = "Henter FFmpeg og FFprobe fra GitHub...";
                    var ffmpegService = new FfmpegUpdateService(_platform);
                    var ffInfo = await ffmpegService.CheckForFfmpegUpdateAsync();
                    var progress = new Progress<string>(status => txtProgress.Text = status);
                    var ffResult = await ffmpegService.UpdateFfmpegAsync(ffInfo, progress);

                    if (!ffResult.Success)
                    {
                        throw new Exception(ffResult.Message);
                    }
                    LogService.Log($"Automatisk installasjon av FFmpeg/FFprobe: {ffResult.Message}", Core.Models.LogLevel.Info);
                }

                // Bekreft at verktøyene nå er på plass
                var recheck = ToolsCheckService.CheckTools(_platform);
                if (recheck.HasMissingTools)
                {
                    string stillMissing = string.Join(", ", recheck.MissingToolNames);
                    throw new Exception($"Følgende filer mangler fortsatt etter nedlasting: {stillMissing}");
                }

                _installedSuccessfully = true;
                _isDownloading = false;
                pbProgress.IsIndeterminate = false;
                pbProgress.Value = 100;
                pbProgress.Foreground = System.Windows.Media.Brushes.LightGreen;
                txtProgress.Text = "✓ Alle nødvendige verktøy er nå på plass og klare til bruk!";
                txtProgress.Foreground = System.Windows.Media.Brushes.LightGreen;
                btnDone.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _isDownloading = false;
                pbProgress.IsIndeterminate = false;
                pbProgress.Foreground = System.Windows.Media.Brushes.IndianRed;
                txtProgress.Text = $"Feil under nedlasting: {ex.Message}";
                txtProgress.Foreground = System.Windows.Media.Brushes.IndianRed;
                pnlActionButtons.Visibility = Visibility.Visible;
                btnDownload.Content = "Prøv igjen";
                LogService.Log($"Feil ved automatisk verktøynedlasting: {ex.Message}", Core.Models.LogLevel.Error);
            }
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ShowManualInstallWarning();
            DialogResult = false;
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isDownloading)
            {
                e.Cancel = true;
                return;
            }

            if (!_installedSuccessfully && !_hasWarnedManual)
            {
                ShowManualInstallWarning();
            }
        }

        private void ShowManualInstallWarning()
        {
            if (_hasWarnedManual) return;
            _hasWarnedManual = true;

            string missingFiles = string.Join("\n", _status.MissingToolNames.Select(n => $"  • {n}"));
            string message =
                "ADVARSEL: Programmet vil ikke virke uten disse verktøyene!\n\n" +
                "Du må selv manuelt laste ned og plassere følgende filer i Tools-mappen:\n\n" +
                $"{missingFiles}\n\n" +
                $"Mappeplassering:\n{_status.ToolsDirectory}\n\n" +
                "Før filene er lagt inn vil du verken kunne analysere lenker eller laste ned innhold fra NRK.\n\n" +
                "Vil du åpne Tools-mappen nå?";

            var res = MessageBox.Show(this, message, "Advarsel – Verktøy mangler", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    if (!Directory.Exists(_status.ToolsDirectory))
                        Directory.CreateDirectory(_status.ToolsDirectory);
                    PlatformService.Instance.OpenFolder(_status.ToolsDirectory);
                }
                catch { }
            }
        }
    }
}
