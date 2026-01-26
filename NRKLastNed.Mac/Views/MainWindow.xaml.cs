using Avalonia.Controls;
using Avalonia.Interactivity;
using NRKLastNed.Mac.ViewModels;

namespace NRKLastNed.Mac.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            // Vi bruker ShowDialog med 'this' som parameter for å sette eier
            var result = await settingsWindow.ShowDialog<bool?>(this);
            if (result == true)
            {
                _vm.RefreshSettings();
            }
        }

        private async void OpenAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            await aboutWindow.ShowDialog(this);
        }
    }
}