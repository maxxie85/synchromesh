using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.Core.Servers;

namespace Synchro.ViewModels;

public sealed partial class ServerSetupViewModel : ObservableValidator
{
    private readonly IServerProfileStore _profiles;
    private readonly ICredentialStore _credentials;
    private readonly IConnectionTester _tester;
    private readonly Services.IFilePicker _files;
    private readonly IPasswordFile _passwordFile;
    private Guid _profileId = Guid.NewGuid();

    public ServerSetupViewModel(
        IServerProfileStore profiles, ICredentialStore credentials, IConnectionTester tester, Services.IFilePicker files,
        IPasswordFile passwordFile)
    {
        _profiles = profiles;
        _credentials = credentials;
        _tester = tester;
        _files = files;
        _passwordFile = passwordFile;

        ErrorsChanged += (_, _) =>
        {
            SaveCommand.NotifyCanExecuteChanged();
            TestCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>Raised when the dialog should close. The argument is the saved profile, or null when cancelled.</summary>
    public event EventHandler<ServerProfile?>? CloseRequested;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestCommand))]
    [Required(ErrorMessage = "Enter the server's address.")]
    [RegularExpression(@"^\s*[^\s/]+\s*$", ErrorMessage = "Use a host name or IP address only, without http:// or a path.")]
    private string _host = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Use a port between 1 and 65535.")]
    private int _port = 9091;

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _useHttps;

    /// <summary>Optional, HTTPS only: for a proxy in front of Transmission that asks for a client certificate.</summary>
    [ObservableProperty] private string _clientCertificatePath = "";

    /// <summary>Empty when the key is in the certificate file, or the certificate is a .p12/.pfx file.</summary>
    [ObservableProperty] private string _clientKeyPath = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [RegularExpression(@"^/\S*$", ErrorMessage = "The RPC path starts with a slash, for example /transmission/rpc.")]
    private string _rpcPath = "/transmission/rpc";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty] private bool _isStatusError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestCommand))]
    private bool _isTesting;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Saving failed for lack of a keyring: offer the app's own encrypted password file.</summary>
    [ObservableProperty] private bool _canUsePasswordFile;

    /// <summary>Fills the form from an existing profile (edit mode). Skip it for a new server.</summary>
    public async Task InitializeAsync(ServerProfile? existing, CancellationToken ct = default)
    {
        if (existing is null) return;

        _profileId = existing.Id;
        Name = existing.Name;
        Host = existing.Host;
        Port = existing.Port;
        UseHttps = existing.UseHttps;
        RpcPath = existing.RpcPath;
        Username = existing.Username ?? "";
        ClientCertificatePath = existing.ClientCertificatePath ?? "";
        ClientKeyPath = existing.ClientKeyPath ?? "";

        try
        {
            Password = await _credentials.GetPasswordAsync(existing.Id, ct) ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Couldn't read the saved password: {ex.Message}", isError: true);
        }
    }

    private bool CanSave() => !IsTesting && !HasErrors && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task TestAsync(CancellationToken ct)
    {
        IsTesting = true;
        SetStatus("Connecting…", isError: false);
        try
        {
            var result = await _tester.TestAsync(BuildProfile(), PasswordOrNull(), ct);
            switch (result)
            {
                case ConnectionTestResult.Success s:
                    SetStatus(s.Capabilities.Warning is { } warning
                        ? $"Connected to Transmission {s.ServerVersion}. {warning}"
                        : $"Connected to Transmission {s.ServerVersion}.", isError: false);
                    break;
                case ConnectionTestResult.Failure f:
                    SetStatus(f.Message, isError: true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(null, isError: false);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        ValidateAllProperties();
        if (HasErrors) return;

        var profile = BuildProfile();
        try
        {
            // Credentials first: if the keychain fails we haven't saved a profile without its password.
            if (profile.Username is null || string.IsNullOrEmpty(Password))
                await _credentials.DeleteAsync(profile.Id, ct);
            else
                await _credentials.SetPasswordAsync(profile.Id, Password, ct);

            await _profiles.SaveAsync(profile, ct);
        }
        catch (KeyringUnavailableException)
        {
            SetStatus("No keyring was found to keep the password. Synchromesh can keep it in its own encrypted file " +
                "on this computer instead. That protects it if the file is copied on its own, but not from other " +
                "programs you run. Or install a keyring (GNOME Keyring, KWallet or KeePassXC), or leave the password empty.",
                isError: true);
            CanUsePasswordFile = true;
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Couldn't save the server: {ex.Message}", isError: true);
            return;
        }

        CanUsePasswordFile = false;
        CloseRequested?.Invoke(this, profile);
    }

    /// <summary>The user agreed to the password file: set it up and save again.</summary>
    [RelayCommand]
    private async Task SaveWithPasswordFileAsync(CancellationToken ct)
    {
        try
        {
            _passwordFile.Enable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Couldn't create the password file: {ex.Message}", isError: true);
            return;
        }

        await SaveAsync(ct);
    }

    [RelayCommand]
    private async Task BrowseCertificateAsync()
    {
        if (await _files.PickCertificateFileAsync("Choose the client certificate") is { } path)
            ClientCertificatePath = path;
    }

    [RelayCommand]
    private async Task BrowseKeyAsync()
    {
        if (await _files.PickCertificateFileAsync("Choose the private key") is { } path)
            ClientKeyPath = path;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);

    private ServerProfile BuildProfile()
    {
        var host = Host.Trim();
        return new ServerProfile(
            _profileId,
            string.IsNullOrWhiteSpace(Name) ? host : Name.Trim(),
            host,
            Port,
            UseHttps,
            RpcPath.Trim(),
            string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
            // Kept while HTTPS is off, so switching it back on doesn't lose them; only used with HTTPS.
            NullIfBlank(ClientCertificatePath),
            NullIfBlank(ClientKeyPath));
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private string? PasswordOrNull() => string.IsNullOrEmpty(Password) ? null : Password;

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        CanUsePasswordFile = false;
    }
}
