using NSubstitute;
using Synchro.Core.Servers;
using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class ServerSetupViewModelTests
{
    private readonly IServerProfileStore _profiles = Substitute.For<IServerProfileStore>();
    private readonly ICredentialStore _credentials = Substitute.For<ICredentialStore>();
    private readonly IConnectionTester _tester = Substitute.For<IConnectionTester>();

    private readonly Synchro.ViewModels.Services.IFilePicker _files = Substitute.For<Synchro.ViewModels.Services.IFilePicker>();
    private readonly IPasswordFile _passwordFile = Substitute.For<IPasswordFile>();

    private ServerSetupViewModel Create() => new(_profiles, _credentials, _tester, _files, _passwordFile);

    [Fact]
    public void Save_needs_an_address()
    {
        var vm = Create();
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Host = "nas.local";

        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("http://nas.local")]
    [InlineData("nas.local/transmission")]
    [InlineData("nas local")]
    public void Rejects_an_address_with_a_scheme_path_or_spaces(string host)
    {
        var vm = Create();

        vm.Host = host;

        Assert.True(vm.HasErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(" nas.local")]
    [InlineData("nas.local ")]
    [InlineData("192.168.1.20")]
    public void Accepts_an_address_with_surrounding_whitespace(string host)
    {
        var vm = Create();

        vm.Host = host;

        Assert.False(vm.HasErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Rejects_a_port_out_of_range()
    {
        var vm = Create();
        vm.Host = "nas.local";

        vm.Port = 70000;

        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task Test_shows_the_server_version_on_success()
    {
        _tester.TestAsync(Arg.Any<ServerProfile>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Success("4.1.0", ServerCapabilities.Baseline));
        var vm = Create();
        vm.Host = "nas.local";

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Contains("4.1.0", vm.StatusMessage);
        Assert.False(vm.IsStatusError);
        Assert.False(vm.IsTesting);
    }

    [Fact]
    public async Task Test_shows_the_failure_message()
    {
        _tester.TestAsync(Arg.Any<ServerProfile>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Failure(ConnectionFailure.AuthenticationFailed, "Bad login"));
        var vm = Create();
        vm.Host = "nas.local";

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Equal("Bad login", vm.StatusMessage);
        Assert.True(vm.IsStatusError);
    }

    [Fact]
    public async Task Save_stores_the_password_in_the_credential_store_and_closes_with_the_profile()
    {
        var vm = Create();
        vm.Host = " nas.local ";
        vm.Username = "admin";
        vm.Password = "secret";
        ServerProfile? closedWith = null;
        vm.CloseRequested += (_, p) => closedWith = p;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(closedWith);
        Assert.Equal("nas.local", closedWith.Host);
        Assert.Equal("nas.local", closedWith.Name); // name defaults to the address
        await _credentials.Received(1).SetPasswordAsync(closedWith.Id, "secret", Arg.Any<CancellationToken>());
        await _profiles.Received(1).SaveAsync(closedWith, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_without_a_username_clears_any_stored_password()
    {
        var vm = Create();
        vm.Host = "nas.local";
        ServerProfile? closedWith = null;
        vm.CloseRequested += (_, p) => closedWith = p;

        await vm.SaveCommand.ExecuteAsync(null);

        await _credentials.Received(1).DeleteAsync(closedWith!.Id, Arg.Any<CancellationToken>());
        await _credentials.DidNotReceiveWithAnyArgs().SetPasswordAsync(default, default!, default);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = Create();
        var closed = false;
        ServerProfile? closedWith = new(Guid.NewGuid(), "x", "x");
        vm.CloseRequested += (_, p) => { closed = true; closedWith = p; };

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(closedWith);
    }

    [Fact]
    public async Task Edit_mode_loads_the_profile_and_its_password()
    {
        var existing = new ServerProfile(Guid.NewGuid(), "NAS", "nas.local", 9092, true, "/rpc", "admin");
        _credentials.GetPasswordAsync(existing.Id, Arg.Any<CancellationToken>()).Returns("secret");
        var vm = Create();

        await vm.InitializeAsync(existing);

        Assert.Equal("nas.local", vm.Host);
        Assert.Equal(9092, vm.Port);
        Assert.True(vm.UseHttps);
        Assert.Equal("secret", vm.Password);
    }

    [Fact]
    public async Task Chosen_certificate_files_are_saved_with_the_profile()
    {
        _files.PickCertificateFileAsync("Choose the client certificate", Arg.Any<CancellationToken>()).Returns("/certs/client.pem");
        _files.PickCertificateFileAsync("Choose the private key", Arg.Any<CancellationToken>()).Returns("/certs/client.key");
        var vm = Create();
        vm.Host = "nas.local";
        vm.UseHttps = true;

        await vm.BrowseCertificateCommand.ExecuteAsync(null);
        await vm.BrowseKeyCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        await _profiles.Received(1).SaveAsync(
            Arg.Is<ServerProfile>(p => p.ClientCertificatePath == "/certs/client.pem" && p.ClientKeyPath == "/certs/client.key"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_shows_the_certificate_files_and_blank_ones_are_not_saved()
    {
        var vm = Create();
        await vm.InitializeAsync(new ServerProfile(Guid.NewGuid(), "NAS", "nas.local", 443, true, ClientCertificatePath: "/certs/client.p12"));

        Assert.Equal("/certs/client.p12", vm.ClientCertificatePath);
        Assert.Equal("", vm.ClientKeyPath);

        vm.ClientCertificatePath = "  ";
        await vm.SaveCommand.ExecuteAsync(null);

        await _profiles.Received(1).SaveAsync(
            Arg.Is<ServerProfile>(p => p.ClientCertificatePath == null && p.ClientKeyPath == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_succeeds_with_a_warning_for_an_untested_rpc_version()
    {
        _tester.TestAsync(Arg.Any<ServerProfile>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Success("5.0.0", new ServerCapabilities(new Version(7, 0, 0))));
        var vm = Create();
        vm.Host = "nas.local";

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Contains("Connected to Transmission 5.0.0", vm.StatusMessage);
        Assert.Contains("some things may not work", vm.StatusMessage);
        Assert.False(vm.IsStatusError);
    }

    [Fact]
    public async Task Without_a_keyring_it_offers_the_password_file_and_saves_with_it()
    {
        _credentials.SetPasswordAsync(Arg.Any<Guid>(), "secret", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new KeyringUnavailableException("No keyring.")), _ => Task.CompletedTask);
        var vm = Create();
        vm.Host = "nas.local";
        vm.Username = "admin";
        vm.Password = "secret";
        ServerProfile? saved = null;
        vm.CloseRequested += (_, p) => saved = p;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.CanUsePasswordFile);
        Assert.Contains("encrypted file", vm.StatusMessage);
        Assert.Null(saved);
        await _profiles.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);

        await vm.SaveWithPasswordFileCommand.ExecuteAsync(null);

        _passwordFile.Received(1).Enable();
        Assert.NotNull(saved);
        Assert.False(vm.CanUsePasswordFile);
    }
}
