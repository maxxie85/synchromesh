using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synchro.AppServices.Credentials;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Xunit;

namespace Synchro.Tests;

public sealed class PasswordFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "synchro-passwords-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _nas = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private PasswordFile Enabled()
    {
        var file = new PasswordFile(_folder);
        file.Enable();
        return file;
    }

    [Fact]
    public void Is_off_until_the_user_agrees()
    {
        Assert.False(new PasswordFile(_folder).IsEnabled);
        Assert.True(Enabled().IsEnabled);
    }

    [Fact]
    public async Task Without_the_key_nothing_can_be_saved()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PasswordFile(_folder).SetPasswordAsync(_nas, "secret"));
    }

    [Fact]
    public async Task Keeps_passwords_encrypted_and_reads_them_back()
    {
        var file = Enabled();

        await file.SetPasswordAsync(_nas, "hunter2-äö");

        Assert.Equal("hunter2-äö", await new PasswordFile(_folder).GetPasswordAsync(_nas));
        var stored = await File.ReadAllTextAsync(Path.Combine(_folder, "passwords.json"));
        Assert.Contains(_nas.ToString("D"), stored);
        Assert.DoesNotContain("hunter2", stored);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("hunter2-äö")), stored);
    }

    [Fact]
    public async Task The_same_password_is_sealed_differently_each_time()
    {
        var file = Enabled();
        var other = Guid.NewGuid();

        await file.SetPasswordAsync(_nas, "same");
        await file.SetPasswordAsync(other, "same");

        var stored = await File.ReadAllTextAsync(Path.Combine(_folder, "passwords.json"));
        var values = stored.Split('"').Where(p => p.Length > 40 && !p.Contains('-')).ToArray();
        Assert.Equal(2, values.Distinct().Count());
    }

    [Fact]
    public async Task An_entry_moved_to_another_server_does_not_decrypt()
    {
        var file = Enabled();
        await file.SetPasswordAsync(_nas, "secret");
        var other = Guid.NewGuid();
        var path = Path.Combine(_folder, "passwords.json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace(_nas.ToString("D"), other.ToString("D")));

        Assert.Null(await file.GetPasswordAsync(other));
    }

    [Fact]
    public async Task A_lost_or_replaced_key_reads_as_no_password()
    {
        var file = Enabled();
        await file.SetPasswordAsync(_nas, "secret");

        File.Delete(Path.Combine(_folder, "passwords.key"));
        Assert.Null(await file.GetPasswordAsync(_nas));

        file.Enable(); // a new, different key
        Assert.Null(await file.GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Delete_removes_only_that_server()
    {
        var file = Enabled();
        var other = Guid.NewGuid();
        await file.SetPasswordAsync(_nas, "a");
        await file.SetPasswordAsync(other, "b");

        await file.DeleteAsync(_nas);

        Assert.False(await file.ContainsAsync(_nas));
        Assert.Equal("b", await file.GetPasswordAsync(other));
    }

    [Fact]
    public async Task Key_and_passwords_are_readable_by_the_user_only()
    {
        if (OperatingSystem.IsWindows()) return;
        var file = Enabled();
        await file.SetPasswordAsync(_nas, "secret");

        const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(privateMode, File.GetUnixFileMode(Path.Combine(_folder, "passwords.key")));
        Assert.Equal(privateMode, File.GetUnixFileMode(Path.Combine(_folder, "passwords.json")));
    }
}

public sealed class PasswordFileMasterPasswordTests : IDisposable
{
    private const int FastIterations = 1_000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "synchro-master-" + Guid.NewGuid().ToString("N"));
    private readonly IMasterPasswordPrompt _prompt = Substitute.For<IMasterPasswordPrompt>();
    private readonly Guid _nas = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private PasswordFile Open(IMasterPasswordPrompt? prompt = null) => new(_folder, prompt ?? _prompt, FastIterations);

    private async Task<PasswordFile> ProtectedAsync(string masterPassword = "correct horse")
    {
        var file = Open();
        file.Enable();
        await file.SetPasswordAsync(_nas, "secret");
        await file.SetMasterPasswordAsync(masterPassword);
        return file;
    }

    [Fact]
    public async Task With_a_master_password_the_key_is_no_longer_stored_as_it_is()
    {
        var file = await ProtectedAsync();

        Assert.True(file.HasMasterPassword);
        var keyFile = await File.ReadAllTextAsync(Path.Combine(_folder, "passwords.key"));
        Assert.Contains("pbkdf2-sha256", keyFile);
        Assert.Equal("secret", await file.GetPasswordAsync(_nas)); // still unlocked in this run
    }

    [Fact]
    public async Task The_next_run_asks_once_and_then_remembers()
    {
        await ProtectedAsync();
        _prompt.AskAsync(null, _folder, Arg.Any<CancellationToken>()).Returns("correct horse");
        var file = Open();

        Assert.Equal("secret", await file.GetPasswordAsync(_nas));
        Assert.Equal("secret", await file.GetPasswordAsync(_nas));

        await _prompt.Received(1).AskAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_master_password_is_asked_again_with_the_reason()
    {
        await ProtectedAsync();
        _prompt.AskAsync(null, _folder, Arg.Any<CancellationToken>()).Returns("wrong one");
        _prompt.AskAsync("That isn't the master password.", _folder, Arg.Any<CancellationToken>()).Returns("correct horse");

        Assert.Equal("secret", await Open().GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Cancelling_the_prompt_leaves_it_locked()
    {
        await ProtectedAsync();
        _prompt.AskAsync(default, default!, default).ReturnsForAnyArgs((string?)null);

        await Assert.ThrowsAsync<PasswordFileLockedException>(() => Open().GetPasswordAsync(_nas));
        await Assert.ThrowsAsync<PasswordFileLockedException>(() => new PasswordFile(_folder).GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Changing_it_re_encrypts_only_the_key()
    {
        var file = await ProtectedAsync("old password");
        var passwords = await File.ReadAllBytesAsync(Path.Combine(_folder, "passwords.json"));

        await file.SetMasterPasswordAsync("new password");

        Assert.Equal(passwords, await File.ReadAllBytesAsync(Path.Combine(_folder, "passwords.json")));
        _prompt.AskAsync(null, _folder, Arg.Any<CancellationToken>()).Returns("old password");
        _prompt.AskAsync("That isn't the master password.", _folder, Arg.Any<CancellationToken>()).Returns("new password");
        Assert.Equal("secret", await Open().GetPasswordAsync(_nas));
        await _prompt.Received(2).AskAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_it_stores_the_key_as_it_is_again()
    {
        var file = await ProtectedAsync();

        await file.SetMasterPasswordAsync(null);

        Assert.False(file.HasMasterPassword);
        Assert.Equal("secret", await new PasswordFile(_folder).GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Servers_without_a_saved_password_need_no_unlocking()
    {
        await ProtectedAsync();

        Assert.False(await Open().ContainsAsync(Guid.NewGuid()));
        Assert.Null(await Open().GetPasswordAsync(Guid.NewGuid()));
        await _prompt.DidNotReceiveWithAnyArgs().AskAsync(default, default!, default);
    }
}

public sealed class AppCredentialStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "synchro-store-" + Guid.NewGuid().ToString("N"));
    private readonly ICredentialStore _keyring = Substitute.For<ICredentialStore>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly PasswordFile _file;
    private readonly AppCredentialStore _store;
    private readonly Guid _nas = Guid.NewGuid();
    private AppSettings _current = new();

    public AppCredentialStoreTests()
    {
        _file = new PasswordFile(_folder);
        _store = new AppCredentialStore(_keyring, _file, _settings);
        _keyring.Name.Returns("the Keychain");
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _current);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => _current = call.Arg<Func<AppSettings, AppSettings>>()(_current));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void NoKeyring()
    {
        var missing = new KeyringUnavailableException("No keyring.");
        _keyring.SetPasswordAsync(default, default!, default).ThrowsAsyncForAnyArgs(missing);
        _keyring.GetPasswordAsync(default, default).ThrowsAsyncForAnyArgs(missing);
        _keyring.DeleteAsync(default, default).ThrowsAsyncForAnyArgs(missing);
    }

    private void FileChosen() => _current = _current with { PasswordStorage = PasswordStorage.File };

    [Fact]
    public async Task Uses_the_keyring_when_there_is_one()
    {
        _keyring.GetPasswordAsync(_nas, Arg.Any<CancellationToken>()).Returns("secret");

        await _store.SetPasswordAsync(_nas, "secret");

        await _keyring.Received(1).SetPasswordAsync(_nas, "secret", Arg.Any<CancellationToken>());
        Assert.Equal("secret", await _store.GetPasswordAsync(_nas));
        Assert.False(_file.IsEnabled);
    }

    [Fact]
    public async Task Without_a_keyring_it_asks_first()
    {
        NoKeyring();

        await Assert.ThrowsAsync<KeyringUnavailableException>(() => _store.SetPasswordAsync(_nas, "secret"));
        Assert.False(await _file.ContainsAsync(_nas));
    }

    [Fact]
    public async Task Without_a_keyring_and_with_consent_it_uses_the_file()
    {
        NoKeyring();
        _file.Enable();

        await _store.SetPasswordAsync(_nas, "secret");

        Assert.Equal("secret", await _store.GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Reading_without_a_keyring_or_file_entry_is_no_password()
    {
        NoKeyring();

        Assert.Null(await _store.GetPasswordAsync(_nas));
    }

    [Fact]
    public async Task Saving_to_a_keyring_that_appeared_later_moves_the_password_out_of_the_file()
    {
        _file.Enable();
        await _file.SetPasswordAsync(_nas, "old");

        await _store.SetPasswordAsync(_nas, "new");

        Assert.False(await _file.ContainsAsync(_nas));
        await _keyring.Received(1).SetPasswordAsync(_nas, "new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_clears_both_and_tolerates_a_missing_keyring()
    {
        NoKeyring();
        _file.Enable();
        await _file.SetPasswordAsync(_nas, "secret");

        await _store.DeleteAsync(_nas);

        Assert.False(await _file.ContainsAsync(_nas));
    }

    [Fact]
    public async Task With_the_file_chosen_the_keyring_is_never_touched()
    {
        FileChosen();

        await _store.SetPasswordAsync(_nas, "secret");
        Assert.Equal("secret", await _store.GetPasswordAsync(_nas));
        Assert.Null(await _store.GetPasswordAsync(Guid.NewGuid()));
        await _store.DeleteAsync(_nas);

        Assert.DoesNotContain(_keyring.ReceivedCalls(), c => c.GetMethodInfo().Name != "get_Name");
    }

    [Fact]
    public async Task Switching_to_the_file_moves_every_password_and_remembers_it()
    {
        var other = Guid.NewGuid();
        var none = Guid.NewGuid();
        _keyring.GetPasswordAsync(_nas, Arg.Any<CancellationToken>()).Returns("a");
        _keyring.GetPasswordAsync(other, Arg.Any<CancellationToken>()).Returns("b");

        await _store.SwitchAsync(PasswordStorage.File, [_nas, other, none]);

        Assert.Equal(PasswordStorage.File, await _store.GetAsync());
        Assert.Equal("a", await _file.GetPasswordAsync(_nas));
        Assert.Equal("b", await _file.GetPasswordAsync(other));
        Assert.False(await _file.ContainsAsync(none));
        await _keyring.Received(1).DeleteAsync(_nas, Arg.Any<CancellationToken>());
        await _keyring.Received(1).DeleteAsync(other, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switching_back_moves_the_passwords_into_the_keyring()
    {
        FileChosen();
        await _store.SetPasswordAsync(_nas, "a");

        await _store.SwitchAsync(PasswordStorage.Keyring, [_nas]);

        Assert.Equal(PasswordStorage.Keyring, await _store.GetAsync());
        await _keyring.Received(1).SetPasswordAsync(_nas, "a", Arg.Any<CancellationToken>());
        Assert.False(await _file.ContainsAsync(_nas));
    }

    [Fact]
    public async Task Switching_back_without_a_keyring_fails_and_loses_nothing()
    {
        FileChosen();
        await _store.SetPasswordAsync(_nas, "a");
        NoKeyring();

        await Assert.ThrowsAsync<KeyringUnavailableException>(() => _store.SwitchAsync(PasswordStorage.Keyring, [_nas]));

        Assert.Equal(PasswordStorage.File, await _store.GetAsync());
        Assert.Equal("a", await _store.GetPasswordAsync(_nas));
    }
}
