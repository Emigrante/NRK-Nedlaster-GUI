using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NRKLastNed.Services
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
            // Prøv å lese Windows app-tema innstilling fra registeret
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object registryValueObject = key.GetValue("AppsUseLightTheme");
                        if (registryValueObject != null)
                        {
                            int registryValue = (int)registryValueObject;
                            if (registryValue > 0) ApplyLightTheme();
                            else ApplyDarkTheme();
                            return;
                        }
                    }
                }
            }
            catch { }
            // Fallback til Dark hvis vi ikke finner systeminnstilling eller feiler
            ApplyDarkTheme();
        }

        private static void ApplyDarkTheme()
        {
            SetResource("BgDark", (Color)ColorConverter.ConvertFromString("#1E1E24"));
            SetResource("BgLight", (Color)ColorConverter.ConvertFromString("#2D2D35"));
            SetResource("BgInput", (Color)ColorConverter.ConvertFromString("#1E1E24"));
            SetResource("AccentColor", (Color)ColorConverter.ConvertFromString("#00E5FF")); // Cyan
            SetResource("AccentDark", (Color)ColorConverter.ConvertFromString("#00B8D4"));
            SetResource("TextColor", Colors.White);
            SetResource("TextMuted", (Color)ColorConverter.ConvertFromString("#AAAAAA"));
            SetResource("BorderColor", (Color)ColorConverter.ConvertFromString("#555555"));
            SetResource("ButtonText", Colors.Black);
        }

        private static void ApplyLightTheme()
        {
            SetResource("BgDark", (Color)ColorConverter.ConvertFromString("#F5F5F5"));
            SetResource("BgLight", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetResource("BgInput", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetResource("AccentColor", (Color)ColorConverter.ConvertFromString("#00B8D4")); // Mørkere cyan
            SetResource("AccentDark", (Color)ColorConverter.ConvertFromString("#0097A7"));
            SetResource("TextColor", Colors.Black);
            SetResource("TextMuted", (Color)ColorConverter.ConvertFromString("#666666"));
            SetResource("BorderColor", (Color)ColorConverter.ConvertFromString("#CCCCCC"));
            SetResource("ButtonText", Colors.White);
        }

        private static void SetResource(string key, Color color)
        {
            // Oppdaterer ressursene i App.xaml "live"
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}