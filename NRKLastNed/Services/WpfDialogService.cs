using System.Threading.Tasks;
using System.Windows;
using NRKLastNed.Core.Contracts;

namespace NRKLastNed.Services
{
    /// <summary>
    /// WPF-implementasjon av IDialogService som bruker standard MessageBox.
    /// </summary>
    public class WpfDialogService : IDialogService
    {
        private readonly Window? _owner;

        public WpfDialogService(Window? owner = null)
        {
            _owner = owner;
        }

        public Task ShowMessageAsync(string message, string title, DialogType type = DialogType.Info)
        {
            var icon = type switch
            {
                DialogType.Warning => MessageBoxImage.Warning,
                DialogType.Error => MessageBoxImage.Error,
                DialogType.Question => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, icon);
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmationAsync(string message, string title)
        {
            var result = MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }
    }
}
