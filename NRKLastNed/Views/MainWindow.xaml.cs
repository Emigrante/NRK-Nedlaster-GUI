using System;
using System.Windows;
using NRKLastNed.Core.Services;
using NRKLastNed.Core.ViewModels;
using NRKLastNed.Services;

namespace NRKLastNed.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;
        private DownloadOutputWindow? _outputWindow;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel(dialogService: new WpfDialogService(this));
            DataContext = _vm;

            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1.0";
            txtWindowTitle.Text = $"NRK Nedlaster v{version}";
            this.Title = $"NRK Nedlaster v{version}";

            // Abonner på nedlastingseventer fra ViewModel
            _vm.DownloadStarted += OnDownloadStarted;
            _vm.DownloadItemStarted += OnDownloadItemStarted;
            _vm.DownloadItemProgress += OnDownloadItemProgress;
            _vm.ItemDetailedProgress += OnItemDetailedProgress;
            _vm.DownloadCompleted += OnDownloadCompleted;
            _vm.RawOutputReceived += OnRawOutputReceived;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckToolsOnStartup();
        }

        private void CheckToolsOnStartup()
        {
            var status = ToolsCheckService.CheckTools();
            if (status.HasMissingTools)
            {
                var missingWin = new MissingToolsWindow(status)
                {
                    Owner = this
                };
                missingWin.ShowDialog();
            }
        }

        private DownloadOutputWindow GetOrCreateOutputWindow()
        {
            if (_outputWindow == null)
            {
                _outputWindow = new DownloadOutputWindow();
                _outputWindow.Owner = this;
            }
            return _outputWindow;
        }

        private void OnDownloadStarted(object? sender, EventArgs e)
        {
            var win = GetOrCreateOutputWindow();
            win.ClearOutput();
            win.AppendLine("─────────────────────────────────────────────────────────────────");
            win.AppendLine($"▶ Nedlasting startet: {DateTime.Now:HH:mm:ss}");
            win.AppendLine("─────────────────────────────────────────────────────────────────");

            var settings = NRKLastNed.Core.Models.AppSettings.Load();
            if (settings.AutoOpenDownloadLog)
            {
                if (!win.IsVisible)
                {
                    win.Show();
                }
                win.Activate();
            }
        }

        private void OnDownloadItemStarted(object? sender, (string Title, int CurrentIndex, int TotalCount) e)
        {
            var win = GetOrCreateOutputWindow();
            win.SetCurrentItem(e.Title, e.CurrentIndex, e.TotalCount);
            win.UpdateTotalProgress(0, e.TotalCount);
            win.AppendLine("");
            win.AppendLine("═════════════════════════════════════════════════════════════════");
            if (e.TotalCount > 1)
                win.AppendLine($"▶ [Fil {e.CurrentIndex}/{e.TotalCount}] {e.Title}");
            else
                win.AppendLine($"▶ {e.Title}");
            win.AppendLine("═════════════════════════════════════════════════════════════════");
        }

        private void OnDownloadItemProgress(object? sender, (string status, double percent) e)
        {
            // Generell fremdrift for element
        }

        private void OnItemDetailedProgress(object? sender, NRKLastNed.Core.Models.ItemProgressEventArgs e)
        {
            _outputWindow?.UpdateStepProgress(e.Status, e.DownloadPercent, e.ProcessingPercent, e.TotalBatchPercent, e.TotalFilesCount);
        }

        private void OnDownloadCompleted(object? sender, EventArgs e)
        {
            _outputWindow?.NotifyDownloadComplete();
        }

        private void OnRawOutputReceived(object? sender, string line)
        {
            _outputWindow?.AppendLine(line);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && _vm.DownloadItems != null)
            {
                bool select = cb.IsChecked == true;
                foreach (var item in _vm.DownloadItems)
                {
                    item.IsSelected = select;
                }
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete && _vm.RemoveItemCommand.CanExecute(null))
            {
                _vm.RemoveItemCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void ShowOutputWindow_Click(object sender, RoutedEventArgs e)
        {
            var win = GetOrCreateOutputWindow();
            win.Show();
            win.Activate();
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
            _vm.RefreshSettings();
            await _vm.CheckAppUpdateSilentAsync();
        }

        private void OpenAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _outputWindow?.ForceClose();
        }
    }
}