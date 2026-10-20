using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Views
{
    public partial class DownloadOutputWindow : Window
    {
        // 35 000 tegn (~70 KB) holder ca. 700 linjer i loggen, men holder strengen
        // trygt under .NET sin Large Object Heap-grense (85 000 bytes).
        private const int MaxLogLength = 35_000;
        private const int TrimTargetLength = 20_000;

        private bool _forceClose = false;
        private int _currentLogLength = 0;

        // Kø og flagg for å samle opp logglinjer i én felles UI-oppdatering
        private readonly ConcurrentQueue<string> _pendingLines = new();
        private int _isFlushScheduled = 0;

        // Mellomlagring av sist viste verdier for å unngå unødvendige UI-oppdateringer og streng-allokeringer
        private string _lastStatus = "";
        private int _lastDownloadWhole = -1;
        private int _lastProcessingWhole = -1;
        private int _lastTotalWhole = -1;
        private int _lastDownloadColorState = 0;   // 0 = ukjent, 1 = Accent, 2 = LightGreen
        private int _lastProcessingColorState = 0; // 0 = ukjent, 1 = Muted, 2 = Accent, 3 = LightGreen

        public DownloadOutputWindow()
        {
            InitializeComponent();

            // Deaktiver Undo-stakken i TextBox for å forhindre at gamle logglinjer blir liggende i RAM
            txtOutput.IsUndoEnabled = false;
            txtOutput.UndoLimit = 0;

            IsVisibleChanged += OnWindowIsVisibleChanged;

            try
            {
                var settings = AppSettings.Load();
                chkAutoOpen.IsChecked = settings.AutoOpenDownloadLog;
                chkAutoClose.IsChecked = settings.AutoCloseDownloadLog;
                chkDebugLog.IsChecked = settings.IsDebugDownloadLog;
            }
            catch
            {
                chkAutoOpen.IsChecked = true;
                chkAutoClose.IsChecked = false;
            }
        }

        private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Hvis vinduet nettopp ble synlig igjen, scroll til bunnen av loggen
            if (IsVisible && _currentLogLength > 0)
            {
                txtOutput.ScrollToEnd();
            }
        }

        /// <summary>
        /// Legger til en linje i terminal-loggen. Trådsikker og samler opp linjer (batching)
        /// for minimalt minne- og CPU-forbruk.
        /// </summary>
        public void AppendLine(string line)
        {
            _pendingLines.Enqueue(line);

            if (Interlocked.CompareExchange(ref _isFlushScheduled, 1, 0) == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, FlushPendingLines);
            }
        }

        private void FlushPendingLines()
        {
            Interlocked.Exchange(ref _isFlushScheduled, 0);

            if (_pendingLines.IsEmpty)
                return;

            var sb = new StringBuilder(256);
            while (_pendingLines.TryDequeue(out string? line))
            {
                sb.AppendLine(line);
            }

            if (sb.Length == 0)
                return;

            // Sjekk lengden via vår egen teller i stedet for txtOutput.Text.Length
            // (unngår at WPF må serialisere hele TextBox-innholdet til en ny streng på hver linje)
            if (_currentLogLength + sb.Length > MaxLogLength)
            {
                string currentText = txtOutput.Text;
                if (currentText.Length > TrimTargetLength)
                {
                    int startIndex = currentText.Length - TrimTargetLength;
                    // Kutt ved nærmeste linjeskift så vi ikke får en halv linje øverst
                    int nextNewLine = currentText.IndexOf('\n', startIndex);
                    if (nextNewLine >= 0 && nextNewLine + 1 < currentText.Length)
                    {
                        startIndex = nextNewLine + 1;
                    }

                    string trimmed = currentText[startIndex..];
                    txtOutput.Text = trimmed;
                    _currentLogLength = trimmed.Length;
                }
                else
                {
                    _currentLogLength = currentText.Length;
                }
            }

            string textToAppend = sb.ToString();
            txtOutput.AppendText(textToAppend);
            _currentLogLength += textToAppend.Length;

            // ScrollToEnd() tvinger frem en synkron layout-beregning i WPF.
            // Kjør den kun når vinduet faktisk er synlig på skjermen.
            if (IsVisible)
            {
                txtOutput.ScrollToEnd();
            }

            // Hvis det kom inn nye linjer mens vi tømte køen, planlegg en ny tømming
            if (!_pendingLines.IsEmpty && Interlocked.CompareExchange(ref _isFlushScheduled, 1, 0) == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, FlushPendingLines);
            }
        }

        /// <summary>
        /// Oppdaterer tittelen på aktivt element i headeren og nullstiller fremdriftsindikatorer.
        /// </summary>
        public void SetCurrentItem(string title, int currentIndex = 0, int totalCount = 0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                lblCurrentItemHeader.Text = (totalCount > 1 && currentIndex > 0)
                    ? $"AKTIV FIL SOM BEARBEIDES (Fil {currentIndex}/{totalCount}):"
                    : "AKTIV FIL SOM BEARBEIDES:";

                txtCurrentItem.Text = title;
                txtCurrentStatus.Text = "Starter...";
                _lastStatus = "Starter...";

                pbDownloadProgress.Value = 0;
                txtDownloadPercent.Text = "0%";
                _lastDownloadWhole = 0;
                _lastDownloadColorState = 0;

                pbProcessingProgress.Value = 0;
                txtProcessingPercent.Text = "Venter...";
                txtProcessingPercent.Foreground = (Brush)FindResource("TextMuted");
                _lastProcessingWhole = -1;
                _lastProcessingColorState = 1;

                pnlTotalProgress.Visibility = totalCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Oppdaterer fremdriftsindikator for total fremdrift (vises ved flere filer).
        /// </summary>
        public void UpdateTotalProgress(double totalPercent, int totalCount)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (totalCount > 1)
                {
                    if (pnlTotalProgress.Visibility != Visibility.Visible)
                        pnlTotalProgress.Visibility = Visibility.Visible;

                    int totalWhole = (int)Math.Round(Math.Clamp(totalPercent, 0, 100));
                    if (totalWhole != _lastTotalWhole)
                    {
                        _lastTotalWhole = totalWhole;
                        txtTotalPercent.Text = $"{totalWhole}%";
                    }
                }
                else if (pnlTotalProgress.Visibility != Visibility.Collapsed)
                {
                    pnlTotalProgress.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Oppdaterer begge fremdriftsindikatorer (nedlasting og rekoding/klargjøring) samt statusmelding og total fremdrift.
        /// </summary>
        public void UpdateStepProgress(string status, double downloadPercent, double processingPercent, double totalBatchPercent = 0, int totalCount = 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (!string.IsNullOrEmpty(status) && !string.Equals(status, _lastStatus, StringComparison.Ordinal))
                {
                    _lastStatus = status;
                    txtCurrentStatus.Text = status;
                }

                // 1. Nedlastingsfremdrift
                double dClamped = Math.Clamp(downloadPercent, 0, 100);
                int dWhole = (int)Math.Round(dClamped);
                if (dWhole != _lastDownloadWhole)
                {
                    _lastDownloadWhole = dWhole;
                    pbDownloadProgress.Value = dClamped;

                    if (dWhole >= 100)
                    {
                        txtDownloadPercent.Text = "100% (Fullført)";
                        if (_lastDownloadColorState != 2)
                        {
                            _lastDownloadColorState = 2;
                            txtDownloadPercent.Foreground = Brushes.LightGreen;
                        }
                    }
                    else
                    {
                        txtDownloadPercent.Text = $"{dWhole}%";
                        if (_lastDownloadColorState != 1)
                        {
                            _lastDownloadColorState = 1;
                            txtDownloadPercent.Foreground = (Brush)FindResource("AccentColor");
                        }
                    }
                }

                // 2. Prosesserings-/rekodingsfremdrift
                double pClamped = Math.Clamp(processingPercent, 0, 100);
                int pWhole = (int)Math.Round(pClamped);
                if (pWhole >= 100)
                {
                    if (_lastProcessingWhole != 100 || _lastProcessingColorState != 3)
                    {
                        _lastProcessingWhole = 100;
                        _lastProcessingColorState = 3;
                        pbProcessingProgress.Value = 100;
                        txtProcessingPercent.Text = "100% (Fullført)";
                        txtProcessingPercent.Foreground = Brushes.LightGreen;
                    }
                }
                else if (pClamped > 0)
                {
                    if (pWhole != _lastProcessingWhole || _lastProcessingColorState != 2)
                    {
                        _lastProcessingWhole = pWhole;
                        pbProcessingProgress.Value = pClamped;
                        txtProcessingPercent.Text = $"{pWhole}%";
                        if (_lastProcessingColorState != 2)
                        {
                            _lastProcessingColorState = 2;
                            txtProcessingPercent.Foreground = (Brush)FindResource("AccentColor");
                        }
                    }
                }
                else
                {
                    pbProcessingProgress.Value = 0;
                    string waitText = dWhole >= 100 ? "Starter..." : "Venter...";
                    if (!string.Equals(txtProcessingPercent.Text, waitText, StringComparison.Ordinal))
                    {
                        txtProcessingPercent.Text = waitText;
                    }
                    if (_lastProcessingColorState != 1)
                    {
                        _lastProcessingColorState = 1;
                        txtProcessingPercent.Foreground = (Brush)FindResource("TextMuted");
                    }
                }

                // 3. Total batch-fremdrift
                if (totalCount > 1)
                {
                    if (pnlTotalProgress.Visibility != Visibility.Visible)
                        pnlTotalProgress.Visibility = Visibility.Visible;

                    int tWhole = (int)Math.Round(Math.Clamp(totalBatchPercent, 0, 100));
                    if (tWhole != _lastTotalWhole)
                    {
                        _lastTotalWhole = tWhole;
                        txtTotalPercent.Text = $"{tWhole}%";
                    }
                }
                else if (pnlTotalProgress.Visibility != Visibility.Collapsed)
                {
                    pnlTotalProgress.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Bakoverkompatibel fremdriftsoppdatering.
        /// </summary>
        public void UpdateProgress(string status, double percent)
        {
            UpdateStepProgress(status, percent, 0);
        }

        /// <summary>
        /// Tømmer terminal-loggen og frigjør strengreferanser.
        /// </summary>
        public void ClearOutput()
        {
            while (_pendingLines.TryDequeue(out _)) { }

            Dispatcher.BeginInvoke(() =>
            {
                txtOutput.Clear();
                _currentLogLength = 0;
            });
        }

        /// <summary>
        /// Kalles når alle nedlastinger er fullført.
        /// Lukker automatisk vinduet hvis "autoclose" er valgt.
        /// </summary>
        public void NotifyDownloadComplete()
        {
            Dispatcher.BeginInvoke(() =>
            {
                txtCurrentItem.Text = "✓ Alle nedlastinger fullført!";
                txtCurrentStatus.Text = "Ferdig";
                _lastStatus = "Ferdig";

                pbDownloadProgress.Value = 100;
                txtDownloadPercent.Text = "100% (Fullført)";
                txtDownloadPercent.Foreground = Brushes.LightGreen;
                _lastDownloadWhole = 100;
                _lastDownloadColorState = 2;

                pbProcessingProgress.Value = 100;
                txtProcessingPercent.Text = "100% (Fullført)";
                txtProcessingPercent.Foreground = Brushes.LightGreen;
                _lastProcessingWhole = 100;
                _lastProcessingColorState = 3;

                txtTotalPercent.Text = "100%";
                txtTotalPercent.Foreground = Brushes.LightGreen;
                _lastTotalWhole = 100;

                AppendLine("─────────────────────────────────────────────────────────────────");
                AppendLine($"✓ Alle oppgaver fullført: {DateTime.Now:HH:mm:ss}");
                AppendLine("─────────────────────────────────────────────────────────────────");

                if (chkAutoClose.IsChecked == true)
                {
                    Hide();
                }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_forceClose)
            {
                // Skjul i stedet for å lukke helt, slik at det kan gjenåpnes
                e.Cancel = true;
                Hide();
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            while (_pendingLines.TryDequeue(out _)) { }
            txtOutput.Clear();
            _currentLogLength = 0;
        }

        private void BtnHide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void ChkAutoOpen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.Load();
                settings.AutoOpenDownloadLog = chkAutoOpen.IsChecked == true;
                AppSettings.Save(settings);
            }
            catch
            {
            }
        }

        private void ChkAutoClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.Load();
                settings.AutoCloseDownloadLog = chkAutoClose.IsChecked == true;
                AppSettings.Save(settings);
            }
            catch
            {
            }
        }

        private void ChkDebugLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.Load();
                settings.DownloadLogLevel = chkDebugLog.IsChecked == true ? "Debug" : "Info";
                AppSettings.Save(settings);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Tvangslukker vinduet (brukes ved applikasjonsavslutning).
        /// </summary>
        public void ForceClose()
        {
            _forceClose = true;
            if (Dispatcher.CheckAccess())
            {
                Close();
            }
            else
            {
                Dispatcher.Invoke(Close);
            }
        }
    }
}