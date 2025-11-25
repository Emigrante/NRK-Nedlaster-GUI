using System.Windows;
using NRKLastNed.Models;
using NRKLastNed.Services;

namespace NRKLastNed
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Last inn innstillinger og sett tema ved oppstart
            var settings = AppSettings.Load();
            ThemeService.ApplyTheme(settings.AppTheme);
        }
    }
}