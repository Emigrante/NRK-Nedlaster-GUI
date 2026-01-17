using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NRKLastNed.Mac.Models;
using NRKLastNed.Mac.Services;

namespace NRKLastNed.Mac.Views
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private UpdateService _toolUpdateService;
        private AppUpdateService _appUpdateService;
        private FfmpegUpdateService _ffmpegUpdateService;

        private AppUpdateService.AppUpdateInfo _pendingAppUpdate;
        private UpdateService.ToolUpdateInfo _pendingYtDlpUpdate;
        private FfmpegUpdateService.FfmpegUpdateInfo _pendingFfmpegUpdate;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            _toolUpdateService = new UpdateService();
            _appUpdateService = new AppUpdateService();
            _ffmpegUpdateService = new FfmpegUpdateService();

            InitializeUI();
            _ = CheckVersionsAsync();
        }

        private void InitializeUI()
        {
            cmbResolution.ItemsSource = new List<string> { "2160", "1440", "1080", "720", "540", "480", "best" };
            cmbTheme.ItemsSource = new List<string> { "System", "Light", "Dark" };
            cmbLogLevel.ItemsSource = Enum.GetValues(typeof(LogLevel));

            txtOutput.Text = _settings.OutputFolder;
            txtTemp.Text = _settings.TempFolder;
            chkUseSystemTemp.IsChecked = _settings.UseSystemTemp;
            pnlCustomTemp.IsEnabled = !_settings.UseSystemTemp;

            cmbResolution.SelectedItem = _settings.DefaultResolution;
            cmbTheme.SelectedItem = _settings.AppTheme;

            chkEnableLog.IsChecked = _settings.EnableLogging;
            cmbLogLevel.SelectedItem = _settings.LogLevel;
        }

        private async Task CheckVersionsAsync()
        {
            lblAppVersion.Text = "Sjekker...";
            btnAppUpdate.IsEnabled = false;

#if DEBUG
            lblAppVersion.Text = "Dev Mode";
            btnAppUpdate.IsEnabled = false;
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

            lblYtDlpVersion.Text = "Sjekker...";
            btnYtDlpUpdate.IsEnabled = false;

            _pendingYtDlpUpdate = await _toolUpdateService.CheckForYtDlpUpdateAsync();

            if (_pendingYtDlpUpdate.CurrentVersion == "Ikke installert" || _pendingYtDlpUpdate.CurrentVersion == "Ukjent")
            {
                lblYtDlpVersion.Text = "Mangler (Må lastes ned)";
                btnYtDlpUpdate.Content = "Last ned";
                btnYtDlpUpdate.IsEnabled = true;
            }
            else if (_pendingYtDlpUpdate.IsNewVersionAvailable)
            {
                lblYtDlpVersion.Text = "Ny versjon tilgjengelig";
                btnYtDlpUpdate.Content = "Oppdater";
                btnYtDlpUpdate.IsEnabled = true;
            }
            else
            {
                lblYtDlpVersion.Text = "Siste versjon installert";
                btnYtDlpUpdate.Content = "Oppdatert";
                btnYtDlpUpdate.IsEnabled = false;
            }

            lblFfmpegVersion.Text = "Sjekker...";
            btnFfmpegUpdate.IsEnabled = false;

            _pendingFfmpegUpdate = await _ffmpegUpdateService.CheckForUpdatesAsync();
            string installedFfmpeg = await _ffmpegUpdateService.GetInstalledVersionAsync();

            if (installedFfmpeg == "Ikke installert")
            {
                lblFfmpegVersion.Text = "Mangler (Må lastes ned)";
                btnFfmpegUpdate.Content = "Last ned";
                btnFfmpegUpdate.IsEnabled = true;
            }
            else if (_pendingFfmpegUpdate.IsNewVersionAvailable)
            {
                lblFfmpegVersion.Text = "Ny versjon tilgjengelig";
                btnFfmpegUpdate.Content = "Oppdater";
                btnFfmpegUpdate.IsEnabled = true;
            }
            else
            {
                lblFfmpegVersion.Text = "Siste versjon installert";
                btnFfmpegUpdate.Content = "Oppdatert";
                btnFfmpegUpdate.IsEnabled = false;
            }
        }

        private async void AppUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAppUpdate == null || !_pendingAppUpdate.IsNewVersionAvailable) return;

            // Simple confirmation dialog
            var result = await ShowMessageBoxAsync($"Vil du oppdatere til {_pendingAppUpdate.LatestVersion}?", "Oppdatering", MessageBoxType.Question);
            if (result == MessageBoxResult.Yes)
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

            string result = await _toolUpdateService.UpdateYtDlpAsync(_pendingYtDlpUpdate);

            await ShowMessageBoxAsync(result, "Status", MessageBoxType.Information);
            await CheckVersionsAsync();
        }

        private async void GetFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            btnFfmpegUpdate.IsEnabled = false;
            lblFfmpegVersion.Text = "Jobber...";

            if (_pendingFfmpegUpdate != null && _pendingFfmpegUpdate.IsNewVersionAvailable)
            {
                var result = await ShowMessageBoxAsync($"Vil du laste ned FFmpeg ({_pendingFfmpegUpdate.LatestVersion})?\nStørrelse: ~80 MB", 
                    "Oppdater FFmpeg", MessageBoxType.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var progress = new Progress<string>(status => lblFfmpegVersion.Text = status);
                    try
                    {
                        await _ffmpegUpdateService.UpdateFfmpegAsync(_pendingFfmpegUpdate, progress);
                        await ShowMessageBoxAsync("FFmpeg er installert/oppdatert!", "Suksess", MessageBoxType.Information);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Feil: {ex.Message}", "Feil", MessageBoxType.Error);
                    }
                }
            }

            await CheckVersionsAsync();
        }

        private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Velg nedlastningsmappe",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    txtOutput.Text = folders[0].Path.LocalPath;
                }
            }
        }

        private async void BrowseTemp_Click(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Velg temp mappe",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    txtTemp.Text = folders[0].Path.LocalPath;
                }
            }
        }

        private void chkUseSystemTemp_Checked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = false;
        private void chkUseSystemTemp_Unchecked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = true;

        private async void Save_Click(object sender, RoutedEventArgs e)
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
            // Close with result true
            Close(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Close with result false
            Close(false);
        }

        private async Task<MessageBoxResult> ShowMessageBoxAsync(string message, string title, MessageBoxType type)
        {
            // For Question type, we need Yes/No buttons
            if (type == MessageBoxType.Question)
            {
                var dialog = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                    CanResize = false
                };
                
                var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
                panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 10) });
                
                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var yesButton = new Button { Content = "Ja", Margin = new Avalonia.Thickness(0, 0, 10, 0), Width = 80 };
                var noButton = new Button { Content = "Nei", Width = 80 };
                
                MessageBoxResult result = MessageBoxResult.No;
                yesButton.Click += (s, e) => { result = MessageBoxResult.Yes; dialog.Close(true); };
                noButton.Click += (s, e) => { result = MessageBoxResult.No; dialog.Close(false); };
                
                buttonPanel.Children.Add(yesButton);
                buttonPanel.Children.Add(noButton);
                panel.Children.Add(buttonPanel);
                dialog.Content = panel;
                
                await dialog.ShowDialog<bool>(this);
                return result;
            }
            else
            {
                // Simple OK dialog
                var dialog = new Window
                {
                    Title = title,
                    Width = 400,
                    Height = 150,
                    Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(20) },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                    CanResize = false
                };
                
                await dialog.ShowDialog(this);
                return MessageBoxResult.OK;
            }
        }
    }

    public enum MessageBoxType
    {
        Information,
        Question,
        Error
    }

    public enum MessageBoxResult
    {
        Yes,
        No,
        OK,
        Cancel
    }
}
