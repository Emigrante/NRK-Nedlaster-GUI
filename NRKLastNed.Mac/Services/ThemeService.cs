using System;
using Avalonia;
using Avalonia.Media;

namespace NRKLastNed.Mac.Services
{
    public static class ThemeService
    {
        public static void ApplyTheme(string themeName)
        {
            if (string.IsNullOrEmpty(themeName) || themeName == "System")
            {
                ApplySystemTheme();
            }
            else if (themeName == "Light")
            {
                ApplyLightTheme();
            }
            else
            {
                ApplyDarkTheme();
            }
        }

        private static void ApplySystemTheme()
        {
            // On macOS, check system theme
            try
            {
                // For now, default to dark theme on macOS
                // In the future, this could check macOS appearance settings
                ApplyDarkTheme();
            }
            catch
            {
                ApplyDarkTheme();
            }
        }

        private static void ApplyDarkTheme()
        {
            SetResource("BgDark", Color.Parse("#1E1E24"));
            SetResource("BgLight", Color.Parse("#2D2D35"));
            SetResource("BgInput", Color.Parse("#1E1E24"));
            SetResource("AccentColor", Color.Parse("#00E5FF"));
            SetResource("AccentDark", Color.Parse("#00B8D4"));
            SetResource("TextColor", Colors.White);
            SetResource("TextMuted", Color.Parse("#AAAAAA"));
            SetResource("BorderColor", Color.Parse("#555555"));
            SetResource("ButtonText", Colors.Black);
        }

        private static void ApplyLightTheme()
        {
            SetResource("BgDark", Color.Parse("#F5F5F5"));
            SetResource("BgLight", Color.Parse("#FFFFFF"));
            SetResource("BgInput", Color.Parse("#FFFFFF"));
            SetResource("AccentColor", Color.Parse("#00B8D4"));
            SetResource("AccentDark", Color.Parse("#0097A7"));
            SetResource("TextColor", Colors.Black);
            SetResource("TextMuted", Color.Parse("#666666"));
            SetResource("BorderColor", Color.Parse("#CCCCCC"));
            SetResource("ButtonText", Colors.White);
        }

        private static void SetResource(string key, Color color)
        {
            if (Application.Current != null)
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
            }
        }
    }
}
