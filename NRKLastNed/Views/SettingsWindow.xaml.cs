using System;
using System.Windows;
using System.Collections.Generic;
using Microsoft.Win32;
using NRKLastNed.Models;
using NRKLastNed.Services;
using System.Diagnostics; // For Process.Start

namespace NRKLastNed.Views
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private UpdateService _updateService; // Ny service

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            _updateService = new UpdateService();

            InitializeUI();
            CheckVersionsAsync(); // Sjekk versjon ved start av vindu
        }

        private void InitializeUI()
        {
            // Populer lister
            cmbResolution.ItemsSource = new List<string> { "2160", "1440", "1080", "720", "540", "480", "best" };
            cmbTheme.ItemsSource = new List<string> { "System", "Light", "Dark" };
            cmbLogLevel.ItemsSource = Enum.GetValues(typeof(LogLevel));

            // Sett verdier
            txtOutput.Text = _settings.OutputFolder;
            txtTemp.Text = _settings.TempFolder;
            chkUseSystemTemp.IsChecked = _settings.UseSystemTemp;
            pnlCustomTemp.IsEnabled = !_settings.UseSystemTemp;

            cmbResolution.SelectedItem = _settings.DefaultResolution;
            cmbTheme.SelectedItem = _settings.AppTheme;

            chkEnableLog.IsChecked = _settings.EnableLogging;
            cmbLogLevel.SelectedItem = _settings.LogLevel;
        }

        // NY: Sjekk versjon av yt-dlp
        private async void CheckVersionsAsync()
        {
            string version = await _updateService.GetYtDlpVersionAsync();
            lblYtDlpVersion.Text = $"Installert versjon: {version}";
        }

        // NY: Oppdater yt-dlp knapp
        private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false; // Hindre dobbelklikk

            lblYtDlpVersion.Text = "Oppdaterer... vennligst vent.";

            string result = await _updateService.UpdateYtDlpAsync();

            MessageBox.Show(result, "Oppdatering Status", MessageBoxButton.OK, MessageBoxImage.Information);

            // Sjekk versjon på nytt
            CheckVersionsAsync();

            if (btn != null) btn.IsEnabled = true;
        }

        // NY: Hent FFmpeg (Åpner nettleser)
        private void GetFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ffmpeg.org/download.html",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtOutput.Text = dialog.FolderName;
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
            _settings.OutputFolder = txtOutput.Text;
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