using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.Core.Servers;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Settings;

/// <summary>
/// Settings > Servers: keep passwords in the system keyring or in the app's own encrypted file, and optionally protect
/// that file with a master password. Switching moves the saved passwords at once; when something fails, the switch
/// goes back and says why.
/// </summary>
public sealed partial class PasswordsViewModel(
    IPasswordStorage storage, IPasswordFile file, IServerProfileStore profiles, IDialogService dialogs) : ObservableObject
{
    /// <summary>Shortest master password accepted.</summary>
    public const int MinimumMasterPasswordLength = 8;

    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileInUse))]
    private bool _useFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeMasterPasswordCommand))]
    private bool _useMasterPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeMasterPasswordCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _error;

    public string Description =>
        $"Instead of {storage.KeyringName}. The passwords are encrypted with a key that only your user account can read: " +
        "safe if the file is copied on its own. Add a master password to also keep them from other programs you run.";

    /// <summary>The file is chosen, or serves as the fallback where there's no keyring: the master password applies.</summary>
    public bool FileInUse => UseFile || file.IsEnabled;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var current = await storage.GetAsync(ct);
        _loading = true;
        UseFile = current == PasswordStorage.File;
        UseMasterPassword = file.HasMasterPassword;
        _loading = false;
        OnPropertyChanged(nameof(FileInUse));
    }

    partial void OnUseFileChanged(bool value)
    {
        if (_loading) return;
        Run(() => SwitchAsync(value ? PasswordStorage.File : PasswordStorage.Keyring));
    }

    partial void OnUseMasterPasswordChanged(bool value)
    {
        if (_loading) return;
        Run(value ? AddMasterPasswordAsync : RemoveMasterPasswordAsync);
    }

    private bool CanChangeMasterPassword() => UseMasterPassword && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChangeMasterPassword))]
    private Task ChangeMasterPasswordAsync() => Switching = RunAsync(async () =>
    {
        if (await AskNewMasterPasswordAsync("Change master password") is { } masterPassword)
            await file.SetMasterPasswordAsync(masterPassword);
    });

    // Internal so tests can await what a toggle starts.
    internal Task? Switching { get; private set; }

    private void Run(Func<Task> action) => Switching = RunAsync(action);

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Error = null;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            // Whatever happened, show how things are now.
            _loading = true;
            UseFile = await storage.GetAsync() == PasswordStorage.File;
            UseMasterPassword = file.HasMasterPassword;
            _loading = false;
            OnPropertyChanged(nameof(FileInUse));
            IsBusy = false;
        }
    }

    private async Task SwitchAsync(PasswordStorage target)
    {
        var ids = (await profiles.LoadAsync()).Select(p => p.Id).ToArray();
        try
        {
            await storage.SwitchAsync(target, ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(target == PasswordStorage.File
                ? $"Couldn't move the passwords to the file: {ex.Message}"
                : $"Couldn't move the passwords to {storage.KeyringName}: {ex.Message}", ex);
        }
    }

    private async Task AddMasterPasswordAsync()
    {
        if (await AskNewMasterPasswordAsync("Set a master password") is { } masterPassword)
        {
            file.Enable(); // the fallback may not have created the key yet
            await file.SetMasterPasswordAsync(masterPassword);
        }
    }

    private async Task RemoveMasterPasswordAsync()
    {
        if (await dialogs.ConfirmAsync("Remove the master password?",
                "The saved passwords stay encrypted in the file, but anything running as you can then read them.",
                "Remove"))
            await file.SetMasterPasswordAsync(null);
    }

    /// <summary>Asks twice; null when the user cancels either time.</summary>
    private async Task<string?> AskNewMasterPasswordAsync(string title)
    {
        var first = await dialogs.PromptAsync(new TextPrompt(title,
            "Synchromesh asks for it once each time it starts, before it connects. It can't be recovered: if you forget " +
            "it, you'll have to enter the server passwords again.",
            "Master password", "Next", Secret: true,
            Validate: text => text.Length < MinimumMasterPasswordLength
                ? $"Use at least {MinimumMasterPasswordLength} characters."
                : null));
        if (first is null) return null;

        return await dialogs.PromptAsync(new TextPrompt(title, "Type it once more to make sure.", "Master password again",
            "Set", Secret: true, Validate: text => text == first ? null : "That's not the same password."));
    }
}
