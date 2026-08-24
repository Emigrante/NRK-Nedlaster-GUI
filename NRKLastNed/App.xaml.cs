using System.Windows;
using NRKLastNed.Core.Models;
using NRKLastNed.Core.Services;
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

            // NYTT: Sjekk om vi nettopp har oppdatert (og vis changelog popup)
            AppUpdateService.ShowReleaseNotesIfJustUpdated();
        }
    }
}