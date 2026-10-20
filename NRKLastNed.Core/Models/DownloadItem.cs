using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NRKLastNed.Core.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _status = "Venter";
        private double _progress;
        private string _fileName = "";
        private string _selectedResolution = "best";
        private string _selectedLanguage = "Norsk";
        private string _selectedCodec = VideoCodecs.H264;
        private ObservableCollection<string> _availableResolutions;
        private ObservableCollection<string> _availableLanguages;
        private ObservableCollection<string> _availableCodecs;
        private bool _isTelevision;
        private bool _isSelected = true;

        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string EpisodeTitle { get; set; } = "";
        public string SeasonEpisode { get; set; } = ""; // F.eks "S01E01"

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public bool IsTelevision
        {
            get => _isTelevision;
            set { _isTelevision = value; OnPropertyChanged(); }
        }

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
            set
            {
                if (_selectedResolution != value)
                {
                    _selectedResolution = value;
                    OnPropertyChanged();
                    UpdateFileName();
                }
            }
        }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set { _selectedLanguage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableCodecs
        {
            get => _availableCodecs;
            set { _availableCodecs = value; OnPropertyChanged(); }
        }

        public string SelectedCodec
        {
            get => _selectedCodec;
            set
            {
                if (_selectedCodec != value)
                {
                    _selectedCodec = value;
                    OnPropertyChanged();
                    UpdateFileName();
                }
            }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Oppdaterer det genererte scenefilnavnet basert på nåværende metadata og brukerens valg.
        /// </summary>
        public void UpdateFileName()
        {
            FileName = Services.FileNameFormatter.BuildOutputFileName(this);
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
            _availableResolutions = new ObservableCollection<string>();

            // Standard sprakliste
            _availableLanguages = new ObservableCollection<string>
            {
                "Norsk",
                "Svensk",
                "Dansk",
                "Engelsk"
            };

            // Kodek-valg (kun relevant for TV-elementer)
            _availableCodecs = new ObservableCollection<string>(VideoCodecs.All);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
