using System;
using System.Windows;
using NRKLastNed.Core.Models;

namespace NRKLastNed.Views
{
    public partial class DownloadOutputWindow : Window
    {
        private const int MaxLogLength = 400_000;
        private bool _forceClose = false;

        public DownloadOutputWindow()
        {
            InitializeComponent();

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

        /// <summary>
        /// Legger til en linje i terminal-loggen. Trådsikker via Dispatcher.
        /// </summary>
        public void AppendLine(string line)
        {
            if (Dispatcher.CheckAccess())
            {
                AppendInternal(line);
            }
            else
            {
                Dispatcher.BeginInvoke(() => AppendInternal(line));
            }
        }

        private void AppendInternal(string line)
        {
            if (txtOutput.Text.Length > MaxLogLength)
            {
                txtOutput.Text = txtOutput.Text[^(MaxLogLength / 2)..];
            }

            txtOutput.AppendText(line + Environment.NewLine);
            txtOutput.ScrollToEnd();
        }

        /// <summary>
        /// Oppdaterer tittelen på aktivt element i headeren og nullstiller fremdriftsindikatorer.
        /// </summary>
        public void SetCurrentItem(string title, int currentIndex = 0, int totalCount = 0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (totalCount > 1 && currentIndex > 0)
                {
                    lblCurrentItemHeader.Text = $"AKTIV FIL SOM BEARBEIDES (Fil {currentIndex}/{totalCount}):";
                }
                else
                {
                    lblCurrentItemHeader.Text = "AKTIV FIL SOM BEARBEIDES:";
                }

                txtCurrentItem.Text = title;
                txtCurrentStatus.Text = "Starter...";
                pbDownloadProgress.Value = 0;
                txtDownloadPercent.Text = "0%";
                pbProcessingProgress.Value = 0;
                txtProcessingPercent.Text = "Venter...";
                txtProcessingPercent.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");

                if (totalCount > 1)
                {
                    pnlTotalProgress.Visibility = Visibility.Visible;
                }
                else
                {
                    pnlTotalProgress.Visibility = Visibility.Collapsed;
                }
            });
        }

        /// <summary>
        /// Oppdaterer fremdriftsindikator for total fremdrift (vises ved flere filer).
        /// </summary>
        public void UpdateTotalProgress(double totalPercent, int totalCount)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (totalCount > 1)
                {
                    pnlTotalProgress.Visibility = Visibility.Visible;
                    double clamped = Math.Clamp(totalPercent, 0, 100);
                    txtTotalPercent.Text = $"{clamped:0}%";
                }
                else
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
            Dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrEmpty(status))
                {
                    txtCurrentStatus.Text = status;
                }

                double dClamped = Math.Clamp(downloadPercent, 0, 100);
                pbDownloadProgress.Value = dClamped;
                if (dClamped >= 100)
                {
                    txtDownloadPercent.Text = "100% (Fullført)";
                    txtDownloadPercent.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    txtDownloadPercent.Text = $"{dClamped:0}%";
                    txtDownloadPercent.Foreground = (System.Windows.Media.Brush)FindResource("AccentColor");
                }

                double pClamped = Math.Clamp(processingPercent, 0, 100);
                pbProcessingProgress.Value = pClamped;
                if (pClamped >= 100)
                {
                    txtProcessingPercent.Text = "100% (Fullført)";
                    txtProcessingPercent.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else if (pClamped > 0)
                {
                    txtProcessingPercent.Text = $"{pClamped:0}%";
                    txtProcessingPercent.Foreground = (System.Windows.Media.Brush)FindResource("AccentColor");
                }
                else
                {
                    txtProcessingPercent.Text = dClamped >= 100 ? "Starter..." : "Venter...";
                    txtProcessingPercent.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
                }

                if (totalCount > 1)
                {
                    pnlTotalProgress.Visibility = Visibility.Visible;
                    double tClamped = Math.Clamp(totalBatchPercent, 0, 100);
                    txtTotalPercent.Text = $"{tClamped:0}%";
                }
                else
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
        /// Tømmer terminal-loggen.
        /// </summary>
        public void ClearOutput()
        {
            Dispatcher.BeginInvoke(() => txtOutput.Clear());
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
                pbDownloadProgress.Value = 100;
                txtDownloadPercent.Text = "100% (Fullført)";
                txtDownloadPercent.Foreground = System.Windows.Media.Brushes.LightGreen;
                pbProcessingProgress.Value = 100;
                txtProcessingPercent.Text = "100% (Fullført)";
                txtProcessingPercent.Foreground = System.Windows.Media.Brushes.LightGreen;
                txtTotalPercent.Text = "100%";
                txtTotalPercent.Foreground = System.Windows.Media.Brushes.LightGreen;
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
            txtOutput.Clear();
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
