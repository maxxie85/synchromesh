using Avalonia.Threading;
using Synchro.Core.Servers;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

/// <summary>Asks for the password file's master password in the shared prompt dialog, on the UI thread.</summary>
public sealed class MasterPasswordPrompt(IDialogService dialogs) : IMasterPasswordPrompt
{
    public Task<string?> AskAsync(string? problem, string folder, CancellationToken ct = default)
    {
        var message = (problem is null ? "" : problem + " ") +
            "Enter the master password to unlock the saved server passwords. Forgot it? Delete passwords.key and " +
            $"passwords.json in {folder}, then enter the server passwords again.";
        var prompt = new TextPrompt("Unlock passwords", message, "Master password", "Unlock", Secret: true);

        // The password file asks from the thread pool.
        return Dispatcher.UIThread.InvokeAsync(() => dialogs.PromptAsync(prompt, ct));
    }
}
