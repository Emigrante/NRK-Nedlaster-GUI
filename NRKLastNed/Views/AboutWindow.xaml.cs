using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace NRKLastNed.Views
{
    public partial class AboutWindow : Window
    {
        public string VersionText => GetVersionString();
        public string ReleaseDateText => GetReleaseDateString();

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeTelemetry(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("NRKLastNed.Assets.cache.dat");
                if (stream != null)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    var w = new Window
                    {
                        Title = "COYS",
                        Width = 350,
                        Height = 350,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = ResizeMode.NoResize,
                        WindowStyle = WindowStyle.ToolWindow,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                        Content = new System.Windows.Controls.Image
                        {
                            Source = bitmap,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin = new Thickness(10)
                        }
                    };
                    w.ShowDialog();
                }
            }
            catch
            {
            }
        }

        public static string GetVersionString()
        {
            var version = typeof(AboutWindow).Assembly.GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}" : "v1.51";
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
