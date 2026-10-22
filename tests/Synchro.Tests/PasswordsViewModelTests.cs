using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synchro.Core.Servers;
using Synchro.ViewModels.Settings;
using Xunit;

namespace Synchro.Tests;

public class PasswordsViewModelTests
{
    private readonly IPasswordStorage _storage = Substitute.For<IPasswordStorage>();
    private readonly IServerProfileStore _profiles = Substitute.For<IServerProfileStore>();
    private readonly IPasswordFile _file = Substitute.For<IPasswordFile>();
    private readonly Synchro.ViewModels.Services.IDialogService _dialogs = Substitute.For<Synchro.ViewModels.Services.IDialogService>();
    private readonly ServerProfile _nas = new(Guid.NewGuid(), "NAS", "nas.local");

    private PasswordsViewModel Create() => new(_storage, _file, _profiles, _dialogs);

    public PasswordsViewModelTests()
    {
        _storage.KeyringName.Returns("the Keychain");
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _nas });
    }

    [Fact]
    public async Task Shows_the_current_choice_without_moving_anything()
    {
        _storage.GetAsync(Arg.Any<CancellationToken>()).Returns(PasswordStorage.File);
        var vm = Create();

        await vm.LoadAsync();

        Assert.True(vm.UseFile);
        Assert.Contains("the Keychain", vm.Description);
        await _storage.DidNotReceiveWithAnyArgs().SwitchAsync(default, default!, default);
    }

    [Fact]
    public async Task Turning_it_on_moves_every_servers_password()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.UseFile = true;
        await vm.Switching!;

        await _storage.Received(1).SwitchAsync(PasswordStorage.File,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == _nas.Id), Arg.Any<CancellationToken>());
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_move_turns_the_switch_back_and_says_why()
    {
        _storage.GetAsync(Arg.Any<CancellationToken>()).Returns(PasswordStorage.File);
        _storage.SwitchAsync(default, default!, default).ThrowsAsyncForAnyArgs(new KeyringUnavailableException("No keyring."));
        var vm = Create();
        await vm.LoadAsync();

        vm.UseFile = false;
        await vm.Switching!;

        Assert.True(vm.UseFile);
        Assert.Equal("Couldn't move the passwords to the Keychain: No keyring.", vm.Error);
    }

    [Fact]
    public async Task A_master_password_is_asked_twice_and_then_set()
    {
        _file.IsEnabled.Returns(true);
        var prompts = new List<ViewModels.TextPrompt>();
        _dialogs.PromptAsync(Arg.Do<ViewModels.TextPrompt>(prompts.Add), Arg.Any<CancellationToken>()).Returns("correct horse");
        var vm = Create();
        await vm.LoadAsync();
        _file.When(f => f.SetMasterPasswordAsync("correct horse", Arg.Any<CancellationToken>()))
            .Do(_ => _file.HasMasterPassword.Returns(true));

        vm.UseMasterPassword = true;
        await vm.Switching!;

        Assert.Equal(2, prompts.Count);
        Assert.All(prompts, p => Assert.True(p.Secret));
        Assert.Equal("Use at least 8 characters.", prompts[0].Validate!("short"));
        Assert.Equal("That's not the same password.", prompts[1].Validate!("correct horsE"));
        Assert.Null(prompts[1].Validate!("correct horse"));
        await _file.Received(1).SetMasterPasswordAsync("correct horse", Arg.Any<CancellationToken>());
        Assert.True(vm.UseMasterPassword);
        Assert.True(vm.ChangeMasterPasswordCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_the_master_password_turns_the_switch_back()
    {
        _file.IsEnabled.Returns(true);
        _dialogs.PromptAsync(Arg.Any<ViewModels.TextPrompt>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        var vm = Create();
        await vm.LoadAsync();

        vm.UseMasterPassword = true;
        await vm.Switching!;

        Assert.False(vm.UseMasterPassword);
        await _file.DidNotReceiveWithAnyArgs().SetMasterPasswordAsync(default, default);
    }

    [Fact]
    public async Task Removing_the_master_password_asks_first()
    {
        _file.IsEnabled.Returns(true);
        _file.HasMasterPassword.Returns(true);
        _dialogs.ConfirmAsync(default!, default!, default!, default).ReturnsForAnyArgs(true);
        var vm = Create();
        await vm.LoadAsync();
        _file.When(f => f.SetMasterPasswordAsync(null, Arg.Any<CancellationToken>()))
            .Do(_ => _file.HasMasterPassword.Returns(false));

        vm.UseMasterPassword = false;
        await vm.Switching!;

        await _file.Received(1).SetMasterPasswordAsync(null, Arg.Any<CancellationToken>());
        Assert.False(vm.UseMasterPassword);
    }

    [Fact]
    public async Task The_master_password_applies_wherever_the_file_is_used()
    {
        _file.IsEnabled.Returns(true); // the no-keyring fallback, with the keyring still chosen
        var vm = Create();

        await vm.LoadAsync();

        Assert.False(vm.UseFile);
        Assert.True(vm.FileInUse);
    }
}
