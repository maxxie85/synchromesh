using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Formatting;
using Xunit;

namespace Synchro.Tests;

public sealed class StorageViewModelTests : IDisposable
{
    private readonly IStorageService _storage = Substitute.For<IStorageService>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly List<StorageViewModel> _created = [];

    private static readonly StorageInfo Roomy = new("/data", 1_000_000_000_000, 2_000_000_000_000);
    private static readonly StorageInfo Full = new("/data", 50_000_000_000, 2_000_000_000_000);

    public StorageViewModelTests() => _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));

    public void Dispose() => _created.ForEach(vm => vm.Dispose());

    private StorageViewModel Create()
    {
        var vm = new StorageViewModel(_storage, _connection, TimeProvider.System);
        _created.Add(vm);
        return vm;
    }

    private void Publish(ConnectionState state)
    {
        _connection.Status.Returns(new ConnectionStatus(state));
        _connection.StatusChanged += Raise.Event();
    }

    [Fact]
    public async Task Shows_used_and_free_space_with_the_total()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(Roomy);
        var vm = Create();

        await vm.RefreshAsync();

        Assert.True(vm.HasStorage);
        Assert.Equal(50, vm.UsedPercent);
        Assert.False(vm.IsLow);
        Assert.Equal($"{ByteFormatter.FormatSize(1_000_000_000_000)} used · {ByteFormatter.FormatSize(1_000_000_000_000)} free", vm.Summary);
        Assert.StartsWith($"of {ByteFormatter.FormatSize(2_000_000_000_000)}", vm.TotalText);
        Assert.Equal("/data", vm.Folder);
    }

    [Fact]
    public async Task Less_than_a_tenth_free_is_marked_low()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(Full);
        var vm = Create();

        await vm.RefreshAsync();

        Assert.True(vm.IsLow);
    }

    [Fact]
    public async Task Without_a_total_it_shows_only_the_free_space()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(new StorageInfo("/data", 500, 0));
        var vm = Create();

        await vm.RefreshAsync();

        Assert.False(vm.HasTotal);
        Assert.Equal($"{ByteFormatter.FormatSize(500)} free", vm.Summary);
    }

    [Fact]
    public async Task A_failed_request_keeps_the_last_values()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(Roomy);
        var vm = Create();
        await vm.RefreshAsync();
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StorageInfo?>(new HttpRequestException("timeout")));

        await vm.RefreshAsync();

        Assert.True(vm.HasStorage);
        Assert.Equal(50, vm.UsedPercent);
    }

    [Fact]
    public async Task Connecting_loads_it_and_switching_server_clears_it()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(Roomy);
        var vm = Create();

        Publish(ConnectionState.Connected);
        await TestWait.UntilAsync(() => vm.HasStorage);

        Publish(ConnectionState.Connecting);
        Assert.False(vm.HasStorage);
    }

    [Fact]
    public async Task It_refreshes_again_after_the_interval()
    {
        _storage.GetDownloadStorageAsync(Arg.Any<CancellationToken>()).Returns(Roomy);
        var vm = Create();
        vm.RefreshInterval = TimeSpan.FromMilliseconds(10);

        Publish(ConnectionState.Connected);
        await TestWait.UntilAsync(() => _storage.ReceivedCalls().Count() >= 3);
        Publish(ConnectionState.Disconnected);
    }
}
