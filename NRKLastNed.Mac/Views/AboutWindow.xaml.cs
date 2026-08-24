using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NRKLastNed.Mac.Views
{
    public partial class AboutWindow : Window
    {
        public string VersionText => GetVersionString();
        public string ReleaseDateText => GetReleaseDateString();

        public AboutWindow()
        {
            DataContext = this;
            AvaloniaXamlLoader.Load(this);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public static string GetVersionString()
        {
            var version = typeof(AboutWindow).Assembly.GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}" : "v1.21";
        }

        public static string GetReleaseDateString()
        {
            try
            {
                var attribute = typeof(AboutWindow).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "BuildDate");

                if (attribute != null && DateTime.TryParse(attribute.Value, out var parsedDate))
                {
                    return parsedDate.ToString("d. MMMM yyyy", new System.Globalization.CultureInfo("nb-NO"));
                }

                string? path = Environment.ProcessPath ?? typeof(AboutWindow).Assembly.Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return File.GetLastWriteTime(path).ToString("d. MMMM yyyy", new System.Globalization.CultureInfo("nb-NO"));
                }
            }
            catch { }

            return DateTime.Now.ToString("d. MMMM yyyy", new System.Globalization.CultureInfo("nb-NO"));
        }
    }
}