using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NRKLastNed.Mac.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
