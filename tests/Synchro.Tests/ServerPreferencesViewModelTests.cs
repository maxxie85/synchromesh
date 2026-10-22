using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;
using Synchro.ViewModels.Settings;
using Xunit;

namespace Synchro.Tests;

public class ServerPreferencesViewModelTests
{
    private readonly IServerPreferencesService _service = Substitute.For<IServerPreferencesService>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private static readonly ServerPreferences Server = new(
        "/data/done", false, "/data/incoming", true, true, false, true, 5, false, 10, true, 2.0, false, 30,
        200, 50, EncryptionMode.Preferred, true, true, false, 51413, true, 50, 20, true, 540, 1020, 62,
        BlocklistEnabled: true, BlocklistUrl: "https://bl.example/level1.gz");

    public ServerPreferencesViewModelTests()
    {
        _connection.ActiveProfile.Returns(new ServerProfile(Guid.NewGuid(), "NAS", "nas.local"));
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(Server);
    }

    private ServerPreferencesViewModel Create() => new(_service, _connection, _dialogs);

    [Fact]
    public async Task Loads_the_servers_preferences_with_times_as_hours_and_minutes()
    {
        var vm = Create();

        await vm.LoadAsync();

        Assert.True(vm.HasPreferences);
        Assert.Contains("NAS", vm.Heading);
        Assert.Equal("09:00", vm.AltSpeedFrom);
        Assert.Equal("17:00", vm.AltSpeedTo);
        Assert.Equal("Weekdays", vm.AltSpeedDays?.Label);
        Assert.Equal(EncryptionMode.Preferred, vm.Encryption?.Value);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Without_a_connection_it_says_so()
    {
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns((ServerPreferences?)null);
        var vm = Create();

        await vm.LoadAsync();

        Assert.False(vm.HasPreferences);
        Assert.Contains("Connect to a server", vm.Heading);
    }

    [Fact]
    public async Task Apply_sends_the_edited_preferences()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.AltSpeedFrom = "7:30";
        vm.Encryption = vm.EncryptionModes.Single(m => m.Value == EncryptionMode.Required);
        vm.SeedQueueEnabled = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        await _service.Received(1).SetAsync(Server with
        {
            AltSpeedTimeBegin = 450, Encryption = EncryptionMode.Required, SeedQueueEnabled = true,
        }, Arg.Any<CancellationToken>());
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("9")]
    [InlineData("09:60")]
    public async Task A_bad_time_blocks_apply(string time)
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.AltSpeedTo = time;

        Assert.True(vm.HasErrors);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_unusual_set_of_days_is_kept_as_custom()
    {
        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(Server with { AltSpeedTimeDays = 2 + 8 }); // Mon + Wed
        var vm = Create();

        await vm.LoadAsync();

        Assert.Equal("Custom days", vm.AltSpeedDays?.Label);
        Assert.Equal(10, vm.AltSpeedDays?.Value);
    }

    [Fact]
    public async Task Revert_brings_back_the_servers_values()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.PeerPort = 6881;

        vm.RevertCommand.Execute(null);

        Assert.Equal(51413, vm.PeerPort);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task The_port_check_reports_each_ip_version()
    {
        _service.TestPortAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PortTestResult("ipv4", true), new PortTestResult("ipv6", null),
        });
        var vm = Create();
        await vm.LoadAsync();

        await vm.TestPortCommand.ExecuteAsync(null);

        Assert.Contains("51413 is open over IPv4", vm.PortTestText);
        Assert.Contains("IPv6: couldn't test", vm.PortTestText);
    }

    [Fact]
    public async Task A_closed_port_says_what_it_means()
    {
        _service.TestPortAsync(Arg.Any<CancellationToken>()).Returns(new[] { new PortTestResult("ipv4", false) });
        var vm = Create();
        await vm.LoadAsync();

        await vm.TestPortCommand.ExecuteAsync(null);

        Assert.Contains("closed", vm.PortTestText);
        Assert.Contains("peers can't connect", vm.PortTestText);
    }

    [Fact]
    public void Times_parse_and_format()
    {
        Assert.True(ServerPreferencesViewModel.TryParseTime(" 7:05 ", out var minutes));
        Assert.Equal(425, minutes);
        Assert.Equal("07:05", ServerPreferencesViewModel.FormatTime(425));
        Assert.False(ServerPreferencesViewModel.TryParseTime("24:00", out _));
    }

    [Fact]
    public async Task Loads_the_blocklist_settings_and_its_rule_count()
    {
        _service.GetBlocklistSizeAsync(Arg.Any<CancellationToken>()).Returns(1234);
        var vm = Create();

        await vm.LoadAsync();

        Assert.True(vm.BlocklistEnabled);
        Assert.Equal("https://bl.example/level1.gz", vm.BlocklistUrl);
        Assert.Equal($"{1234:N0} rules loaded.", vm.BlocklistText);
        Assert.True(vm.UpdateBlocklistCommand.CanExecute(null));
    }

    [Fact]
    public async Task Update_now_reports_the_new_rule_count()
    {
        _service.UpdateBlocklistAsync(Arg.Any<CancellationToken>()).Returns(1);
        var vm = Create();
        await vm.LoadAsync();

        await vm.UpdateBlocklistCommand.ExecuteAsync(null);

        Assert.Equal("1 rule loaded.", vm.BlocklistText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_update_says_what_to_check()
    {
        _service.UpdateBlocklistAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("gotaddrinfo failed.")));
        var vm = Create();
        await vm.LoadAsync();

        await vm.UpdateBlocklistCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't update the blocklist: gotaddrinfo failed. Check the address, and that the server can reach it.", vm.BlocklistText);
    }

    [Fact]
    public async Task Update_now_waits_for_an_address_and_for_unapplied_edits()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.BlocklistUrl = "https://other.example/list.gz";
        Assert.True(vm.IsDirty);
        Assert.False(vm.UpdateBlocklistCommand.CanExecute(null)); // the server would use the old address

        _service.GetAsync(Arg.Any<CancellationToken>()).Returns(Server with { BlocklistUrl = "" });
        await vm.LoadAsync();
        Assert.False(vm.UpdateBlocklistCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("ftp://bl.example/list")]
    [InlineData("bl.example/list")]
    public async Task A_blocklist_address_must_be_http_or_https(string url)
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.BlocklistUrl = url;

        Assert.True(vm.HasErrors);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_sends_the_blocklist_settings()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.BlocklistEnabled = false;
        vm.BlocklistUrl = " https://other.example/list.gz ";
        await vm.ApplyCommand.ExecuteAsync(null);

        await _service.Received(1).SetAsync(Server with
        {
            BlocklistEnabled = false, BlocklistUrl = "https://other.example/list.gz",
        }, Arg.Any<CancellationToken>());
        Assert.True(vm.UpdateBlocklistCommand.CanExecute(null));
    }
}

public class ServerPreferencesServiceTests
{
    [Fact]
    public async Task An_ip_version_that_cant_be_tested_is_reported_not_thrown()
    {
        var client = Substitute.For<ITransmissionClient>();
        client.TestPortAsync("ipv4", Arg.Any<CancellationToken>()).Returns(true);
        client.TestPortAsync("ipv6", Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new HttpRequestException("no ipv6")));
        var connection = Substitute.For<IConnectionService>();
        connection.Client.Returns(client);

        var results = await new ServerPreferencesService(connection).TestPortAsync();

        Assert.Equal(new[] { new PortTestResult("ipv4", true), new PortTestResult("ipv6", null) }, results);
    }

    [Fact]
    public async Task The_blocklist_goes_to_the_connected_server()
    {
        var client = Substitute.For<ITransmissionClient>();
        client.GetBlocklistSizeAsync(Arg.Any<CancellationToken>()).Returns(10);
        client.UpdateBlocklistAsync(Arg.Any<CancellationToken>()).Returns(20);
        var connection = Substitute.For<IConnectionService>();
        connection.Client.Returns(client);
        var service = new ServerPreferencesService(connection);

        Assert.Equal(10, await service.GetBlocklistSizeAsync());
        Assert.Equal(20, await service.UpdateBlocklistAsync());
    }

    [Fact]
    public async Task Updating_the_blocklist_without_a_connection_explains_it()
    {
        var connection = Substitute.For<IConnectionService>();
        connection.Client.Returns((ITransmissionClient?)null);
        var service = new ServerPreferencesService(connection);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateBlocklistAsync());
        Assert.Equal("Not connected to a server.", ex.Message);
    }
}
