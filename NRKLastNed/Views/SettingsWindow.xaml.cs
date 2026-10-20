using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using Microsoft.Win32;
using NRKLastNed.Core.Models;
using NRKLastNed.Core.Services;
using NRKLastNed.Services;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace NRKLastNed.Views
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private UpdateService _toolUpdateService;
        private AppUpdateService _appUpdateService;
        private FfmpegUpdateService _ffmpegUpdateService;

        private AppUpdateInfo? _pendingAppUpdate;
        private ToolUpdateInfo? _pendingYtDlpUpdate;
        private FfmpegUpdateInfo? _pendingFfmpegUpdate;

        private bool _isInitializing = true;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            _toolUpdateService = new UpdateService();
            _appUpdateService = new AppUpdateService();
            _ffmpegUpdateService = new FfmpegUpdateService();

            this.Loaded += (s, e) => InitializeUI();

            _ = CheckVersionsAsync();
        }

        private void InitializeUI()
        {
            cmbResolution.ItemsSource = new List<string> { "2160", "1440", "1080", "720", "540", "480", "best" };
            cmbTheme.ItemsSource = new List<string> { "System", "Lys", "Mørk" };
            cmbLogLevel.ItemsSource = Enum.GetValues(typeof(LogLevel));
            cmbDefaultCodec.ItemsSource = VideoCodecs.All;

            // NY: Initialiser TV/Radio-mapper
            txtTvOutput.Text = _settings.TvOutputFolder;
            txtRadioOutput.Text = _settings.RadioOutputFolder;
            chkUseSameFolder.IsChecked = _settings.UseSameFolderForBoth;

            // Oppdater synlighet av Radio-mappe basert på delt-mappe checkbox
            UpdateRadioFolderVisibility();

            txtTemp.Text = _settings.TempFolder;
            chkUseSystemTemp.IsChecked = _settings.UseSystemTemp;
            pnlCustomTemp.IsEnabled = !_settings.UseSystemTemp;

            cmbResolution.SelectedItem = _settings.DefaultResolution;
            cmbDefaultCodec.SelectedItem = _settings.DefaultCodec;
            chkAutoOpenDownloadLog.IsChecked = _settings.AutoOpenDownloadLog;
            if (string.Equals(_settings.DownloadLogLevel, "Debug", StringComparison.OrdinalIgnoreCase))
                cmbDownloadLogLevel.SelectedIndex = 1;
            else
                cmbDownloadLogLevel.SelectedIndex = 0;

            chkHardwareAcceleration.IsChecked = _settings.UseHardwareAcceleration;
            _ = UpdateGpuDetectionLabelAsync();
            
            // Map saved theme to Lys/Mørk
            string loadedTheme = _settings.AppTheme;
            if (loadedTheme == "Light") loadedTheme = "Lys";
            if (loadedTheme == "Dark") loadedTheme = "Mørk";
            cmbTheme.SelectedItem = loadedTheme;

            chkEnableLog.IsChecked = _settings.EnableLogging;
            cmbLogLevel.SelectedItem = _settings.LogLevel;
            
            _isInitializing = false;
        }

        private void UpdateRadioFolderVisibility()
        {
            bool isShared = chkUseSameFolder.IsChecked == true;
            lblTvOutput.Text = isShared ? "TV- og radiomappe:" : "TV-mappe:";
            lblTvOutput.Visibility = Visibility.Visible;
            gridTvOutput.Visibility = Visibility.Visible;

            var radioVisibility = isShared ? Visibility.Collapsed : Visibility.Visible;
            lblRadioOutput.Visibility = radioVisibility;
            gridRadioOutput.Visibility = radioVisibility;
        }

        private async Task CheckVersionsAsync()
        {
            // --- 1. SJEKK APP OPPDATERING ---
            lblAppVersion.Text = "Sjekker...";
            btnAppUpdate.IsEnabled = false;

            _pendingAppUpdate = await _appUpdateService.CheckForAppUpdatesAsync();

            if (_pendingAppUpdate != null && _pendingAppUpdate.IsNewVersionAvailable)
            {
                lblAppVersion.Text = $"Ny versjon tilgjengelig ({_pendingAppUpdate.LatestVersion})";
                btnAppUpdate.Content = "Oppdater";
                btnAppUpdate.IsEnabled = true;
            }
            else
            {
                lblAppVersion.Text = "Siste versjon installert";
                btnAppUpdate.Content = "Oppdatert";
                btnAppUpdate.IsEnabled = false;
            }

            // --- 2. SJEKK YT-DLP OPPDATERING ---
            lblYtDlpVersion.Text = "Sjekker...";
            btnYtDlpUpdate.IsEnabled = false;

            _pendingYtDlpUpdate = await _toolUpdateService.CheckForYtDlpUpdateAsync();

            if (_pendingYtDlpUpdate != null && (_pendingYtDlpUpdate.CurrentVersion == "Ikke installert" || _pendingYtDlpUpdate.CurrentVersion == "Ukjent"))
            {
                // STATUS: MANGLER
                lblYtDlpVersion.Text = "Mangler (Må lastes ned)";
                btnYtDlpUpdate.Content = "Last ned";
                btnYtDlpUpdate.IsEnabled = true;
            }
            else if (_pendingYtDlpUpdate != null && _pendingYtDlpUpdate.IsNewVersionAvailable)
            {
                // STATUS: NY VERSJON
                lblYtDlpVersion.Text = "Ny versjon tilgjengelig";
                btnYtDlpUpdate.Content = "Oppdater";
                btnYtDlpUpdate.IsEnabled = true;
            }
            else
            {
                // STATUS: OPPDATERT
                lblYtDlpVersion.Text = "Siste versjon installert";
                btnYtDlpUpdate.Content = "Oppdatert";
                btnYtDlpUpdate.IsEnabled = false;
            }


            // --- 3. SJEKK FFMPEG OPPDATERING ---
            lblFfmpegVersion.Text = "Sjekker...";
            btnFfmpegUpdate.IsEnabled = false;

            _pendingFfmpegUpdate = await _ffmpegUpdateService.CheckForUpdatesAsync();
            string installedFfmpeg = await _ffmpegUpdateService.GetInstalledVersionAsync();

            if (installedFfmpeg == "Ikke installert")
            {
                // STATUS: MANGLER
                lblFfmpegVersion.Text = "Mangler (Må lastes ned)";
                btnFfmpegUpdate.Content = "Last ned";
                btnFfmpegUpdate.IsEnabled = true;
            }
            else if (_pendingFfmpegUpdate != null && _pendingFfmpegUpdate.IsNewVersionAvailable)
            {
                // STATUS: NY VERSJON
                lblFfmpegVersion.Text = "Ny versjon tilgjengelig";
                btnFfmpegUpdate.Content = "Oppdater";
                btnFfmpegUpdate.IsEnabled = true;
            }
            else
            {
                // STATUS: OPPDATERT
                lblFfmpegVersion.Text = "Siste versjon installert";
                btnFfmpegUpdate.Content = "Oppdatert";
                btnFfmpegUpdate.IsEnabled = false;
            }
        }

        private async void AppUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAppUpdate == null || !_pendingAppUpdate.IsNewVersionAvailable) return;

            string promptMessage = AppUpdateService.FormatUpdatePromptMessage(_pendingAppUpdate);

            var res = MessageBox.Show(
                promptMessage,
                $"Oppdatering tilgjengelig (v{_pendingAppUpdate.LatestVersion})",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                btnAppUpdate.IsEnabled = false;
                lblAppVersion.Text = "Laster ned...";
                await _appUpdateService.PerformAppUpdateAsync(_pendingAppUpdate, () =>
                {
                    Application.Current.Shutdown();
                });
            }
        }

        private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
        {
            btnYtDlpUpdate.IsEnabled = false;
            lblYtDlpVersion.Text = "Jobber...";

            // Send med info slik at vi kan laste ned hvis den mangler
            string result = await _toolUpdateService.UpdateYtDlpAsync(_pendingYtDlpUpdate);

            MessageBox.Show(result, "Status", MessageBoxButton.OK, MessageBoxImage.Information);

            await CheckVersionsAsync();
        }

        private async void GetFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            btnFfmpegUpdate.IsEnabled = false;
            lblFfmpegVersion.Text = "Jobber...";

            if (_pendingFfmpegUpdate != null && _pendingFfmpegUpdate.IsNewVersionAvailable)
            {
                var res = MessageBox.Show($"Vil du laste ned FFmpeg ({_pendingFfmpegUpdate.LatestVersion})?\nStørrelse: ~80 MB",
                                          "Oppdater FFmpeg", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    var progress = new Progress<string>(status => lblFfmpegVersion.Text = status);
                    try
                    {
                        var updateResult = await _ffmpegUpdateService.UpdateFfmpegAsync(_pendingFfmpegUpdate, progress);

                        if (updateResult.ChecksumVerified)
                        {
                            MessageBox.Show(
                                $"FFmpeg er installert og oppdatert!\n\n" +
                                $"✓ SHA-256 Hash kontrollert og verifisert:\n{updateResult.Sha256}\n\n" +
                                $"Filens integritet og ekthet er bekreftet mot GitHub release checksums.",
                                "FFmpeg verifisert",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show(
                                $"FFmpeg er installert og oppdatert!\n\nBeregnet SHA-256 Hash:\n{updateResult.Sha256}",
                                "Oppdatering fullført",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Feil under oppdatering: {ex.Message}", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            await CheckVersionsAsync();
        }

        // NY: TV-mappe browser
        private void BrowseTvOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = chkUseSameFolder.IsChecked == true
                    ? "Velg felles nedlastningsmappe for TV og radio"
                    : "Velg TV-nedlastningsmappe"
            };
            if (dialog.ShowDialog() == true) txtTvOutput.Text = dialog.FolderName;
        }

        // NY: Radio-mappe browser
        private void BrowseRadioOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtRadioOutput.Text = dialog.FolderName;
        }

        // NY: Checkbox for delt mappe
        private void chkUseSameFolder_Checked(object sender, RoutedEventArgs e)
        {
            UpdateRadioFolderVisibility();
            LiveSave_Trigger(sender, e);
        }

        private void chkUseSameFolder_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateRadioFolderVisibility();
            LiveSave_Trigger(sender, e);
        }

        private void BrowseTemp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtTemp.Text = dialog.FolderName;
        }

        private void chkUseSystemTemp_Checked(object sender, RoutedEventArgs e)
        {
            pnlCustomTemp.IsEnabled = false;
            LiveSave_Trigger(sender, e);
        }

        private void chkUseSystemTemp_Unchecked(object sender, RoutedEventArgs e)
        {
            pnlCustomTemp.IsEnabled = true;
            LiveSave_Trigger(sender, e);
        }

        private void LiveSave_Trigger(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveSettingsLive();
        }

        private void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (cmbTheme.SelectedItem is string theme)
            {
                _settings.AppTheme = theme;
                ThemeService.ApplyTheme(theme);
            }
            
            SaveSettingsLive();
        }

        private void SaveSettingsLive()
        {
            _settings.TvOutputFolder = txtTvOutput.Text;
            _settings.RadioOutputFolder = txtRadioOutput.Text;
            _settings.UseSameFolderForBoth = chkUseSameFolder.IsChecked == true;
            _settings.OutputFolder = txtTvOutput.Text; // Legacy
            _settings.TempFolder = txtTemp.Text;
            _settings.UseSystemTemp = chkUseSystemTemp.IsChecked == true;

            if (cmbResolution.SelectedItem is string resolution)
                _settings.DefaultResolution = resolution;

            if (cmbDefaultCodec.SelectedItem is string codec)
                _settings.DefaultCodec = codec;

            _settings.AutoOpenDownloadLog = chkAutoOpenDownloadLog.IsChecked == true;
            if (cmbDownloadLogLevel.SelectedItem is ComboBoxItem logItem && logItem.Tag is string tag)
                _settings.DownloadLogLevel = tag;

            _settings.UseHardwareAcceleration = chkHardwareAcceleration.IsChecked == true;

            _settings.EnableLogging = chkEnableLog.IsChecked == true;
            if (cmbLogLevel.SelectedItem is LogLevel level) _settings.LogLevel = level;

            AppSettings.Save(_settings);
        }

        private async Task UpdateGpuDetectionLabelAsync()
        {
            try
            {
                string ffmpegPath = PlatformService.Instance.GetToolPath("ffmpeg");
                var caps = await HardwareEncoderService.GetCapabilitiesAsync(ffmpegPath);
                _ = Dispatcher.BeginInvoke(() =>
                {
                    lblGpuDetection.Text = $"Oppdaget: {caps.Description}";
                });
            }
            catch
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    lblGpuDetection.Text = "Kunne ikke sjekke GPU";
                });
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}