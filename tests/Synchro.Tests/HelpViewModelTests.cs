using NSubstitute;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class HelpViewModelTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IUrlLauncher _launcher = Substitute.For<IUrlLauncher>();
    private readonly IStallLogging _stallLog = Substitute.For<IStallLogging>();
    private readonly Synchro.Core.Settings.IAppSettingsStore _settings = Substitute.For<Synchro.Core.Settings.IAppSettingsStore>();

    private HelpViewModel Create() => new(_dialogs, _launcher, _stallLog, _settings);

    [Fact]
    public async Task Log_ui_stalls_turns_on_and_is_remembered_for_the_next_start()
    {
        var settings = new Synchro.Core.Settings.AppSettings();
        _settings.UpdateAsync(Arg.Any<Func<Synchro.Core.Settings.AppSettings, Synchro.Core.Settings.AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => settings = call.Arg<Func<Synchro.Core.Settings.AppSettings, Synchro.Core.Settings.AppSettings>>()(settings));
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleUiStallLogCommand.ExecuteAsync(null);

        _stallLog.Received(1).SetEnabled(true);
        Assert.True(settings.LogUiStalls);
        Assert.Contains(nameof(HelpViewModel.LogUiStalls), changed);
    }

    [Fact]
    public async Task Log_ui_stalls_turns_off_again()
    {
        _stallLog.IsEnabled.Returns(true);

        await Create().ToggleUiStallLogCommand.ExecuteAsync(null);

        _stallLog.Received(1).SetEnabled(false);
    }

    [Fact]
    public async Task About_shows_the_about_dialog()
    {
        await Create().AboutCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowAboutAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Help_links_open_in_the_browser()
    {
        _launcher.OpenAsync(Arg.Any<Uri>()).Returns(true);
        var vm = Create();

        await vm.OpenTransmissionWebsiteCommand.ExecuteAsync(null);
        await vm.OpenRpcSpecificationCommand.ExecuteAsync(null);

        await _launcher.Received(1).OpenAsync(HelpViewModel.TransmissionWebsite);
        await _launcher.Received(1).OpenAsync(HelpViewModel.RpcSpecification);
        await _dialogs.DidNotReceiveWithAnyArgs().ShowMessageAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_link_that_cannot_be_opened_is_explained_with_the_address()
    {
        _launcher.OpenAsync(Arg.Any<Uri>()).Returns(false);

        await Create().OpenTransmissionWebsiteCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync("Couldn't open the link",
            Arg.Is<string>(m => m.Contains("transmissionbt.com")), Arg.Any<CancellationToken>());
    }
}
