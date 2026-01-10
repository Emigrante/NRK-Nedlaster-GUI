using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NRKLastNed.Mac.Services;
using NRKLastNed.Mac.Views;

namespace NRKLastNed.Mac
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Last inn innstillinger og sett tema ved oppstart
                var settings = Models.AppSettings.Load();
                ThemeService.ApplyTheme(settings.AppTheme);

                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
