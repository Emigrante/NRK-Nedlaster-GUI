using System;

namespace NRKLastNed.Core.Models
{
    /// <summary>
    /// Detaljert fremdriftsinformasjon for en aktiv fil, fordelt på nedlasting og bearbeiding/rekoding.
    /// </summary>
    public class ItemProgressEventArgs : EventArgs
    {
        public string Status { get; set; } = "";
        public double DownloadPercent { get; set; }
        public double ProcessingPercent { get; set; }
        public double OverallPercent { get; set; }
        public double TotalBatchPercent { get; set; }
        public int CurrentFileIndex { get; set; }
        public int TotalFilesCount { get; set; }
    }
}
