using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class AddTorrentFlowTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private static readonly AddTorrentSource.Magnet MagnetA = new("magnet:?xt=urn:btih:aaa", null);
    private static readonly AddTorrentSource.Magnet MagnetB = new("magnet:?xt=urn:btih:bbb", null);

    public AddTorrentFlowTests()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _dialogs.ShowAddTorrentAsync(Arg.Any<AddTorrentSource?>(), Arg.Any<CancellationToken>())
            .Returns(new AddTorrentResult(AddTorrentOutcome.Added, "h", "Name"));
    }

    private AddTorrentFlow Create() => new(_dialogs, _clipboard, _connection);

    [Fact]
    public async Task Manual_add_opens_an_empty_dialog()
    {
        await Create().AddManuallyAsync();

        await _dialogs.Received(1).ShowAddTorrentAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_source_gets_its_own_dialog_in_order()
    {
        var order = new List<AddTorrentSource?>();
        _dialogs.ShowAddTorrentAsync(Arg.Do<AddTorrentSource?>(s => order.Add(s)), Arg.Any<CancellationToken>())
            .Returns(new AddTorrentResult(AddTorrentOutcome.Added, "h", "Name"));

        await Create().AddAsync([MagnetA, MagnetB]);

        Assert.Equal(new AddTorrentSource?[] { MagnetA, MagnetB }, order);
    }

    [Fact]
    public async Task A_cancelled_dialog_does_not_stop_the_next_one()
    {
        _dialogs.ShowAddTorrentAsync(MagnetA, Arg.Any<CancellationToken>()).Returns((AddTorrentResult?)null);

        await Create().AddAsync([MagnetA, MagnetB]);

        await _dialogs.Received(1).ShowAddTorrentAsync(MagnetB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_recognisable_in_a_drop_says_so()
    {
        await Create().AddAsync([]);

        await _dialogs.Received(1).ShowMessageAsync("Nothing to add", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dialogs.DidNotReceiveWithAnyArgs().ShowAddTorrentAsync(default, default);
    }

    [Fact]
    public async Task A_duplicate_is_explained()
    {
        _dialogs.ShowAddTorrentAsync(Arg.Any<AddTorrentSource?>(), Arg.Any<CancellationToken>())
            .Returns(new AddTorrentResult(AddTorrentOutcome.Duplicate, "h", "Ubuntu"));

        await Create().AddAsync([MagnetA]);

        await _dialogs.Received(1).ShowMessageAsync(
            "Already on the server", Arg.Is<string>(m => m.Contains("Ubuntu")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paste_opens_the_dialog_for_a_magnet_link_on_the_clipboard()
    {
        _clipboard.GetTextAsync(Arg.Any<CancellationToken>()).Returns("  magnet:?xt=urn:btih:ccc  ");

        await Create().AddFromClipboardAsync();

        await _dialogs.Received(1).ShowAddTorrentAsync(
            Arg.Is<AddTorrentSource?>(s => s is AddTorrentSource.Magnet), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("just some text")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Paste_explains_when_the_clipboard_has_no_torrent_link(string? text)
    {
        _clipboard.GetTextAsync(Arg.Any<CancellationToken>()).Returns(text);

        await Create().AddFromClipboardAsync();

        await _dialogs.Received(1).ShowMessageAsync(
            "No torrent link on the clipboard", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dialogs.DidNotReceiveWithAnyArgs().ShowAddTorrentAsync(default, default);
    }

    [Fact]
    public async Task Nothing_is_added_while_disconnected()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));
        var flow = Create();

        await flow.AddManuallyAsync();
        await flow.AddAsync([MagnetA]);
        await flow.AddFromClipboardAsync();

        await _dialogs.DidNotReceiveWithAnyArgs().ShowAddTorrentAsync(default, default);
        await _dialogs.Received(3).ShowMessageAsync("Not connected", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
