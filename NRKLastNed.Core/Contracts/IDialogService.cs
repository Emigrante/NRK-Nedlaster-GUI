using System.Threading.Tasks;

namespace NRKLastNed.Core.Contracts
{
    public enum DialogType
    {
        Info,
        Warning,
        Error,
        Question
    }

    public interface IDialogService
    {
        Task ShowMessageAsync(string message, string title, DialogType type = DialogType.Info);
        Task<bool> ShowConfirmationAsync(string message, string title);
    }
}
