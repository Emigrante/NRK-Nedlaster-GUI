using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NRKLastNed.Mac.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _status;
        private double _progress;
        private string _fileName;
        private string _selectedResolution;
        private string _selectedLanguage;
        private ObservableCollection<string> _availableResolutions;
        private ObservableCollection<string> _availableLanguages;

        public string Url { get; set; }
        public string Title { get; set; }
        public string SeasonEpisode { get; set; } // F.eks "S01E01"
        public bool IsSelected { get; set; } = true; // For checkbox i listen

        public ObservableCollection<string> AvailableResolutions
        {
            get => _availableResolutions;
            set { _availableResolutions = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableLanguages
        {
            get => _availableLanguages;
            set { _availableLanguages = value; OnPropertyChanged(); }
        }

        public string SelectedResolution
        {
            get => _selectedResolution;
            set { _selectedResolution = value; OnPropertyChanged(); }
        }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set { _selectedLanguage = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public DownloadItem()
        {
            AvailableResolutions = new ObservableCollection<string>();

            // Standard språkliste
            AvailableLanguages = new ObservableCollection<string>
            {
                "Norsk",
                "Svensk",
                "Dansk",
                "Engelsk",
                "Ukjent"
            };
            SelectedLanguage = "Norsk"; // Standardvalg
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
