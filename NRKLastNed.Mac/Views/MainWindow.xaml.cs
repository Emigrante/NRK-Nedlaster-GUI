using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NRKLastNed.Core.ViewModels;

namespace NRKLastNed.Mac.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            // Vi bruker ShowDialog med 'this' som parameter for å sette eier
            await settingsWindow.ShowDialog<bool?>(this);
            _vm.RefreshSettings();
            await _vm.CheckAppUpdateSilentAsync();
        }

        private async void OpenAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            await aboutWindow.ShowDialog(this);
        }
    }
}
