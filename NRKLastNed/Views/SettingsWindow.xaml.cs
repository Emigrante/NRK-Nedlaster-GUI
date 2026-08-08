using System;
using System.Windows;
using System.Collections.Generic;
using Microsoft.Win32;
using NRKLastNed.Models;
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

        private AppUpdateService.AppUpdateInfo _pendingAppUpdate;
        private UpdateService.ToolUpdateInfo _pendingYtDlpUpdate; // NY
        private FfmpegUpdateService.FfmpegUpdateInfo _pendingFfmpegUpdate;

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
            cmbTheme.ItemsSource = new List<string> { "System", "Light", "Dark" };
            cmbLogLevel.ItemsSource = Enum.GetValues(typeof(LogLevel));

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
            cmbTheme.SelectedItem = _settings.AppTheme;

            chkEnableLog.IsChecked = _settings.EnableLogging;
            cmbLogLevel.SelectedItem = _settings.LogLevel;
        }

        private void UpdateRadioFolderVisibility()
        {
            bool isShared = chkUseSameFolder.IsChecked ?? false;
            lblRadioOutput.Visibility = isShared ? Visibility.Collapsed : Visibility.Visible;
            gridRadioOutput.Visibility = isShared ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task CheckVersionsAsync()
        {
            // --- 1. SJEKK APP OPPDATERING ---
            lblAppVersion.Text = "Sjekker...";
            btnAppUpdate.IsEnabled = false;

#if DEBUG
            lblAppVersion.Text = "Dev Mode";
            btnAppUpdate.ToolTip = "Oppdatering deaktivert i debug.";
#else
            _pendingAppUpdate = await _appUpdateService.CheckForAppUpdatesAsync();

            if (_pendingAppUpdate.IsNewVersionAvailable)
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
#endif

            // --- 2. SJEKK YT-DLP OPPDATERING ---
            lblYtDlpVersion.Text = "Sjekker...";
            btnYtDlpUpdate.IsEnabled = false;

            _pendingYtDlpUpdate = await _toolUpdateService.CheckForYtDlpUpdateAsync();

            if (_pendingYtDlpUpdate.CurrentVersion == "Ikke installert" || _pendingYtDlpUpdate.CurrentVersion == "Ukjent")
            {
                // STATUS: MANGLER
                lblYtDlpVersion.Text = "Mangler (Må lastes ned)";
                btnYtDlpUpdate.Content = "Last ned";
                btnYtDlpUpdate.IsEnabled = true;
            }
            else if (_pendingYtDlpUpdate.IsNewVersionAvailable)
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
            else if (_pendingFfmpegUpdate.IsNewVersionAvailable)
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

            var res = MessageBox.Show($"Vil du oppdatere til {_pendingAppUpdate.LatestVersion}?",
                                      "Oppdatering", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                btnAppUpdate.IsEnabled = false;
                lblAppVersion.Text = "Laster ned...";
                await _appUpdateService.PerformAppUpdateAsync(_pendingAppUpdate);
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
                        await _ffmpegUpdateService.UpdateFfmpegAsync(_pendingFfmpegUpdate, progress);
                        MessageBox.Show("FFmpeg er installert/oppdatert!", "Suksess", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Feil: {ex.Message}", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            await CheckVersionsAsync();
        }

        // NY: TV-mappe browser
        private void BrowseTvOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
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
        }

        private void chkUseSameFolder_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateRadioFolderVisibility();
        }

        private void BrowseTemp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtTemp.Text = dialog.FolderName;
        }

        private void chkUseSystemTemp_Checked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = false;
        private void chkUseSystemTemp_Unchecked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // NY: Lagre TV/Radio-mapper
            _settings.TvOutputFolder = txtTvOutput.Text;
            _settings.RadioOutputFolder = txtRadioOutput.Text;
            _settings.UseSameFolderForBoth = chkUseSameFolder.IsChecked == true;

            // Legacy - for bakoverkompatibilitet
            _settings.OutputFolder = txtTvOutput.Text;

            _settings.TempFolder = txtTemp.Text;
            _settings.UseSystemTemp = chkUseSystemTemp.IsChecked == true;

            if (cmbResolution.SelectedItem != null)
                _settings.DefaultResolution = cmbResolution.SelectedItem.ToString();

            if (cmbTheme.SelectedItem != null)
            {
                _settings.AppTheme = cmbTheme.SelectedItem.ToString();
                ThemeService.ApplyTheme(_settings.AppTheme);
            }

            _settings.EnableLogging = chkEnableLog.IsChecked == true;
            if (cmbLogLevel.SelectedItem is LogLevel level) _settings.LogLevel = level;

            AppSettings.Save(_settings);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}