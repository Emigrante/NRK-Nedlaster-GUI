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
            settingsWindow.Owner = this;
            var result = await settingsWindow.ShowDialog<bool?>(this);
            if (result == true)
            {
                _vm.RefreshSettings();
            }
        }

        private async void OpenAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            await aboutWindow.ShowDialog(this);
        }
    }
}
