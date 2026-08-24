namespace NRKLastNed.Core.Contracts
{
    public interface IThemeService
    {
        string CurrentTheme { get; }
        void ApplyTheme(string themeName);
    }
}
