using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class SpeedLimitServiceTests
{
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITransmissionClient _client = Substitute.For<ITransmissionClient>();
    private static readonly SpeedLimits Limits = new(true, 1000, false, 50, false);

    [Fact]
    public async Task Reads_and_writes_through_the_active_client()
    {
        _connection.Client.Returns(_client);
        _client.GetSpeedLimitsAsync(Arg.Any<CancellationToken>()).Returns(Limits);
        var service = new SpeedLimitService(_connection);

        Assert.Equal(Limits, await service.GetAsync());
        await service.SetAsync(Limits);

        await _client.Received(1).SetSpeedLimitsAsync(Limits, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_clearly_when_not_connected()
    {
        _connection.Client.Returns((ITransmissionClient?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SpeedLimitService(_connection).GetAsync());
    }
}
