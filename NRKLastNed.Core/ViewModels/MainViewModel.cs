using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NRKLastNed.Core.Contracts;
using NRKLastNed.Core.Models;
using NRKLastNed.Core.Services;

namespace NRKLastNed.Core.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private AppSettings _settings;
        private YtDlpService _service;
        private readonly IPlatformService _platform;
        private readonly IDialogService? _dialogService;
        private readonly AppUpdateService _appUpdateService;

        private string _inputUrl = "";
        private ObservableCollection<DownloadItem> _downloadItems = new ObservableCollection<DownloadItem>();
        private string _statusMessage = "";
        private string _batchStatusMessage = "";
        private double _totalProgress;
        private bool _isProgressIndeterminate;
        private DownloadItem? _selectedGridItem;

        private bool _isDownloading;
        private bool _isAnalyzing;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _analysisCts;
        private string _startButtonText = "Start nedlasting";

        private bool _isTelevision = true;
        private bool _isRadio = false;

        private string _updateNotificationText = "";
        private AppUpdateInfo? _pendingAppUpdate;

        private readonly PropertyBatcher _progressBatcher = new PropertyBatcher(debounceMs: 50);

        public MainViewModel(IPlatformService? platform = null, IDialogService? dialogService = null)
        {
            _platform = platform ?? PlatformService.Instance;
            _dialogService = dialogService;
            _settings = AppSettings.Load();
            _service = new YtDlpService(_settings, _platform);
            _appUpdateService = new AppUpdateService(_platform, _dialogService);

            DownloadItems = new SuspendableObservableCollection<DownloadItem>();

            _progressBatcher.PropertyChanged += (s, e) => { if (e.PropertyName != null) OnPropertyChanged(e.PropertyName); };

            AddCommand = new AsyncRelayCommand(async (o) => await AddAndAnalyzeAsync(), (o) => !IsDownloading && !IsAnalyzing);
            DownloadCommand = new AsyncRelayCommand(async (o) => await ToggleDownloadAsync());
            RemoveItemCommand = new RelayCommand((o) => RemoveItem(), (o) => SelectedGridItem != null && !IsDownloading && !IsAnalyzing);
            RemoveFinishedCommand = new RelayCommand((o) => RemoveFinishedItems(), (o) => !IsDownloading && !IsAnalyzing);
            OpenFolderCommand = new RelayCommand((o) => OpenDownloadFolder());
            ApplyUpdateCommand = new AsyncRelayCommand(async (o) => await ApplyPendingUpdateAsync());

            LogService.Log("Applikasjon startet", LogLevel.Info, _settings);

            _ = CheckAppUpdateSilentAsync();
        }

        public AppSettings Settings => _settings;

        public bool IsTelevision
        {
            get => _isTelevision;
            set
            {
                if (_isTelevision != value)
                {
                    _isTelevision = value;
                    _isRadio = !value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRadio));
                }
            }
        }

        public bool IsRadio
        {
            get => _isRadio;
            set
            {
                if (_isRadio != value)
                {
                    _isRadio = value;
                    _isTelevision = !value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTelevision));
                }
            }
        }

        public string InputUrl
        {
            get => _inputUrl;
            set
            {
                _inputUrl = value;
                OnPropertyChanged();

                if (!string.IsNullOrWhiteSpace(_inputUrl))
                {
                    if (_inputUrl.Contains("radio.nrk.no", StringComparison.OrdinalIgnoreCase))
                    {
                        IsRadio = true;
                    }
                    else if (_inputUrl.Contains("tv.nrk.no", StringComparison.OrdinalIgnoreCase))
                    {
                        IsTelevision = true;
                    }
                }
            }
        }

        public ObservableCollection<DownloadItem> DownloadItems
        {
            get => _downloadItems;
            set { _downloadItems = value; OnPropertyChanged(); }
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

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set { _isProgressIndeterminate = value; OnPropertyChanged(); }
        }

        public DownloadItem? SelectedGridItem
        {
            get => _selectedGridItem;
            set
            {
                _selectedGridItem = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string UpdateNotificationText
        {
            get => _updateNotificationText;
            set { _updateNotificationText = value; OnPropertyChanged(); }
        }

        public AppUpdateInfo? PendingAppUpdate
        {
            get => _pendingAppUpdate;
            set { _pendingAppUpdate = value; OnPropertyChanged(); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                _isDownloading = value;
                OnPropertyChanged();
                UpdateStartButtonText();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                _isAnalyzing = value;
                OnPropertyChanged();
                UpdateStartButtonText();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void UpdateStartButtonText()
        {
            StartButtonText = (_isDownloading || _isAnalyzing) ? "Avbryt" : "Start nedlasting";
        }

        public string StartButtonText
        {
            get => _startButtonText;
            set { _startButtonText = value; OnPropertyChanged(); }
        }

        public ICommand AddCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand RemoveFinishedCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ApplyUpdateCommand { get; }

        public void RefreshSettings()
        {
            _settings = AppSettings.Load();
            _service = new YtDlpService(_settings, _platform);
            LogService.Log("Innstillinger oppdatert", LogLevel.Info, _settings);
        }

        public async Task CheckAppUpdateSilentAsync()
        {
            try
            {
                var updateInfo = await _appUpdateService.CheckForAppUpdatesAsync();
                if (updateInfo != null && updateInfo.IsNewVersionAvailable)
                {
                    PendingAppUpdate = updateInfo;
                    UpdateNotificationText = $"Ny versjon ({updateInfo.LatestVersion}) er tilgjengelig! Klikk for a oppdatere.";
                }
            }
            catch { }
        }

        public async Task ApplyPendingUpdateAsync()
        {
            if (PendingAppUpdate != null)
            {
                if (_dialogService != null)
                {
                    string promptMessage = AppUpdateService.FormatUpdatePromptMessage(PendingAppUpdate);
                    bool proceed = await _dialogService.ShowConfirmationAsync(
                        promptMessage,
                        $"Oppdatering tilgjengelig (v{PendingAppUpdate.LatestVersion})");

                    if (!proceed) return;
                }

                await _appUpdateService.PerformAppUpdateAsync(PendingAppUpdate);
            }
        }

        private void OpenDownloadFolder()
        {
            try
            {
                string folder = IsTelevision ? _settings.TvOutputFolder : _settings.RadioOutputFolder;
                if (_settings.UseSameFolderForBoth || string.IsNullOrEmpty(folder))
                {
                    folder = _settings.TvOutputFolder;
                }

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                _platform.OpenFolder(folder);
            }
            catch (Exception ex)
            {
                LogService.Log($"Feil ved apning av nedlastingsmappe: {ex.Message}", LogLevel.Error, _settings);
            }
        }

        private void RemoveItem()
        {
            if (SelectedGridItem != null)
            {
                DownloadItems.Remove(SelectedGridItem);
                SelectedGridItem = null;
            }
        }

        private void RemoveFinishedItems()
        {
            var finished = DownloadItems.Where(i => i.Status == "Ferdig").ToList();
            foreach (var item in finished)
            {
                DownloadItems.Remove(item);
            }
        }

        private async Task AddAndAnalyzeAsync()
        {
            if (string.IsNullOrWhiteSpace(InputUrl)) return;

            if (!_service.CheckTools(out string missingTool))
            {
                StatusMessage = $"Mangler nodvendig verktoy ({Path.GetFileName(missingTool)}). Sjekk Innstillinger.";
                return;
            }

            string urlToProcess = InputUrl.Trim();
            InputUrl = "";

            StatusMessage = "Analyserer URL...";
            BatchStatusMessage = "Henter oversikt over innhold...";
            TotalProgress = 0;
            IsProgressIndeterminate = true;
            _analysisCts = new CancellationTokenSource();
            var token = _analysisCts.Token;
            IsAnalyzing = true;

            try
            {
                var analysisProgress = new Progress<YtDlpService.AnalysisProgressInfo>(update =>
                {
                    if (!string.IsNullOrWhiteSpace(update.StatusMessage))
                    {
                        StatusMessage = update.StatusMessage;
                    }

                    if (!string.IsNullOrWhiteSpace(update.DetailMessage))
                    {
                        BatchStatusMessage = update.DetailMessage;
                    }

                    IsProgressIndeterminate = update.IsIndeterminate;
                    TotalProgress = update.IsIndeterminate ? 0 : update.ProgressPercent;

                    if (update.Item != null)
                    {
                        DownloadItems.Add(update.Item);
                    }
                });

                var items = await _service.AnalyzeUrlAsync(urlToProcess, analysisProgress, token);

                if (items.Count == 0)
                {
                    StatusMessage = "Fant ingen videoer pa URL.";
                    BatchStatusMessage = "";
                    TotalProgress = 0;
                    IsProgressIndeterminate = false;
                    return;
                }

                StatusMessage = $"La til {items.Count} videoer.";
                BatchStatusMessage = $"Analyse ferdig. Fant {items.Count} videoer.";
                TotalProgress = 100;
                IsProgressIndeterminate = false;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Analyse avbrutt.";
                BatchStatusMessage = "";
                TotalProgress = 0;
                IsProgressIndeterminate = false;
            }
            finally
            {
                IsAnalyzing = false;
                _analysisCts?.Dispose();
                _analysisCts = null;
            }
        }

        private async Task ToggleDownloadAsync()
        {
            if (IsAnalyzing)
            {
                if (_analysisCts != null)
                {
                    _analysisCts.Cancel();
                    StatusMessage = "Avbryter analyse...";
                    BatchStatusMessage = "Stopper...";
                }
                return;
            }

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
            if (itemsToDownload.Count == 0)
            {
                StatusMessage = "Ingen valgte elementer a laste ned.";
                return;
            }

            if (!_service.CheckTools(out string missingTool))
            {
                StatusMessage = $"Mangler nodvendig verktoy ({Path.GetFileName(missingTool)}). Sjekk Innstillinger.";
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsDownloading = true;

            int totalCount = itemsToDownload.Count;
            int currentIndex = 0;
            TotalProgress = 0;
            IsProgressIndeterminate = false;

            LogService.Log($"Starter batch nedlasting av {totalCount} filer", LogLevel.Info, _settings);

            try
            {
                foreach (var item in itemsToDownload)
                {
                    if (token.IsCancellationRequested) break;

                    currentIndex++;
                    BatchStatusMessage = $"Laster ned fil {currentIndex} av {totalCount}...";
                    StatusMessage = item.Title;

                    item.Status = "Laster ned...";
                    item.Progress = 0;

                    var progressText = new Progress<string>(t =>
                    {
                        item.Status = t;
                        _progressBatcher.QueueNotification(nameof(DownloadItems));
                    });

                    var progressPercent = new Progress<double>(p =>
                    {
                        item.Progress = p;

                        double overallProgress = ((currentIndex - 1) * 100.0 + p) / totalCount;
                        TotalProgress = overallProgress;

                        _progressBatcher.QueueNotification(nameof(TotalProgress));
                        _progressBatcher.QueueNotification(nameof(DownloadItems));
                    });

                    try
                    {
                        await _service.DownloadItemAsync(item, progressText, progressPercent, token);
                        item.Status = "Ferdig";
                        item.Progress = 100;
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = "Avbrutt";
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "Feilet";
                        LogService.Log($"Feil under nedlasting av {item.Title}: {ex.Message}", LogLevel.Error, _settings);
                    }
                }

                if (token.IsCancellationRequested)
                {
                    StatusMessage = "Nedlasting avbrutt av bruker.";
                    BatchStatusMessage = "";
                }
                else
                {
                    StatusMessage = "Alle nedlastinger fullfort!";
                    BatchStatusMessage = "";
                    TotalProgress = 100;
                }
            }
            finally
            {
                IsDownloading = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
