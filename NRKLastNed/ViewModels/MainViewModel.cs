using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Linq;
using NRKLastNed.Models;
using NRKLastNed.Services;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Threading;

namespace NRKLastNed.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private AppSettings _settings;
        private YtDlpService _service;
        private string _inputUrl;
        private ObservableCollection<DownloadItem> _downloadItems;
        private string _statusMessage;
        private string _batchStatusMessage;
        private double _totalProgress;
        private DownloadItem _selectedGridItem;

        private bool _isDownloading;
        private CancellationTokenSource _cts;
        private string _startButtonText = "START NEDLASTING";

        public MainViewModel()
        {
            _settings = AppSettings.Load();
            _service = new YtDlpService(_settings);

            DownloadItems = new ObservableCollection<DownloadItem>();

            AddCommand = new RelayCommand(async (o) => await AddAndAnalyzeAsync());
            DownloadCommand = new RelayCommand(async (o) => await ToggleDownloadAsync());
            RemoveItemCommand = new RelayCommand((o) => RemoveItem(), (o) => SelectedGridItem != null && !IsDownloading);
            RemoveFinishedCommand = new RelayCommand((o) => RemoveFinishedItems(), (o) => !IsDownloading);
            OpenFolderCommand = new RelayCommand((o) => OpenDownloadFolder());

            LogService.Log("Applikasjon startet", LogLevel.Info, _settings);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                _isDownloading = value;
                OnPropertyChanged();
                StartButtonText = _isDownloading ? "AVBRYT" : "START NEDLASTING";
            }
        }

        public string StartButtonText
        {
            get => _startButtonText;
            set { _startButtonText = value; OnPropertyChanged(); }
        }

        public string InputUrl
        {
            get => _inputUrl;
            set { _inputUrl = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DownloadItem> DownloadItems
        {
            get => _downloadItems;
            set { _downloadItems = value; OnPropertyChanged(); }
        }

        public DownloadItem SelectedGridItem
        {
            get => _selectedGridItem;
            set { _selectedGridItem = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string BatchStatusMessage
        {
            get => _batchStatusMessage;
            set { _batchStatusMessage = value; OnPropertyChanged(); }
        }

        public double TotalProgress
        {
            get => _totalProgress;
            set { _totalProgress = value; OnPropertyChanged(); }
        }

        public RelayCommand AddCommand { get; }
        public RelayCommand DownloadCommand { get; }
        public RelayCommand RemoveItemCommand { get; }
        public RelayCommand RemoveFinishedCommand { get; }
        public RelayCommand OpenFolderCommand { get; }

        public void RefreshSettings()
        {
            _settings = AppSettings.Load();
            _service = new YtDlpService(_settings);
            LogService.Log("Innstillinger oppdatert", LogLevel.Info, _settings);
        }

        private void RemoveItem()
        {
            if (SelectedGridItem != null) DownloadItems.Remove(SelectedGridItem);
        }

        private void RemoveFinishedItems()
        {
            var finished = DownloadItems.Where(i => i.Status == "Ferdig").ToList();
            foreach (var item in finished) DownloadItems.Remove(item);
        }

        private void OpenDownloadFolder()
        {
            if (System.IO.Directory.Exists(_settings.OutputFolder)) Process.Start("explorer.exe", _settings.OutputFolder);
            else MessageBox.Show("Mappen finnes ikke ennå.", "Info");
        }

        private async Task AddAndAnalyzeAsync()
        {
            if (string.IsNullOrWhiteSpace(InputUrl)) return;
            if (IsDownloading) { MessageBox.Show("Kan ikke legge til mens nedlasting pågår."); return; }

            string urlToProcess = InputUrl;
            InputUrl = "";

            StatusMessage = "Sjekker verktøy...";
            if (!_service.ValidateTools(out string msg))
            {
                MessageBox.Show(msg, "Mangler verktøy", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Mangler verktøy - se 'Tools' mappe.";
                return;
            }

            StatusMessage = "Analyserer URL...";
            var items = await _service.AnalyzeUrlAsync(urlToProcess);

            if (items.Count == 0) { StatusMessage = "Fant ingen videoer på URL."; return; }
            foreach (var item in items) DownloadItems.Add(item);
            StatusMessage = $"La til {items.Count} videoer.";
        }

        private async Task ToggleDownloadAsync()
        {
            if (IsDownloading)
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    StatusMessage = "Avbryter...";
                    BatchStatusMessage = "Stopper...";
                }
                return;
            }

            var itemsToDownload = DownloadItems.Where(i => i.IsSelected && i.Status != "Ferdig").ToList();
            int totalCount = itemsToDownload.Count;
            if (totalCount == 0)
            {
                StatusMessage = "Ingen videoer valgt for nedlasting.";
                return;
            }

            IsDownloading = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            StatusMessage = "Starter nedlasting...";
            BatchStatusMessage = $"Laster ned fil 1 av {totalCount} (Total: 0%)";
            LogService.Log($"Starter batch nedlasting av {totalCount} filer", LogLevel.Info, _settings);

            double itemWeight = 100.0 / totalCount;
            double currentBaseProgress = 0;
            int currentCount = 0;

            try
            {
                foreach (var item in itemsToDownload)
                {
                    if (token.IsCancellationRequested) break;

                    currentCount++;

                    item.Status = "Forbereder...";

                    var pText = new Progress<string>(t => {
                        item.Status = t;
                        StatusMessage = $"[{currentCount}/{totalCount}] {item.Title}: {t}";
                    });

                    var pPercent = new Progress<double>(p => {
                        item.Progress = p;

                        // Beregn total progresjon for hele batchen
                        // p er 0-100 for filen.
                        double batchProgress = currentBaseProgress + (p * (itemWeight / 100.0));
                        TotalProgress = batchProgress;

                        // Oppdater teksten nede med både fil-teller og total prosent
                        BatchStatusMessage = $"Laster ned fil {currentCount} av {totalCount} (Total: {batchProgress:0}%)";
                    });

                    try
                    {
                        await _service.DownloadItemAsync(item, pText, pPercent, token);
                        item.Status = "Ferdig";
                        item.Progress = 100;
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = "Avbrutt";
                        StatusMessage = "Nedlasting avbrutt av bruker.";
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "Feilet";
                        LogService.Log($"Feil under nedlasting av {item.Title}: {ex.Message}", LogLevel.Error, _settings);
                    }

                    currentBaseProgress += itemWeight;
                    TotalProgress = currentBaseProgress;
                    BatchStatusMessage = $"Ferdig med fil {currentCount} av {totalCount} (Total: {currentBaseProgress:0}%)";
                }
            }
            finally
            {
                IsDownloading = false;
                _cts.Dispose();
                _cts = null;

                if (StatusMessage != "Nedlasting avbrutt av bruker.")
                {
                    StatusMessage = "Alle operasjoner fullført!";
                    BatchStatusMessage = "Ferdig! (Total: 100%)";
                    TotalProgress = 100;
                }
                else
                {
                    BatchStatusMessage = "Stoppet.";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}