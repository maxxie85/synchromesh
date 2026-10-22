using System.Collections.Concurrent;
using System.Text;
using Synchro.Desktop.Services;
using Xunit;

namespace Synchro.Tests;

public sealed class SingleInstanceTests : IDisposable
{
    // A unique id per test run, so tests never meet each other or a running app.
    private readonly string _appId = $"tm-tests-{Guid.NewGuid():N}";
    private readonly List<SingleInstance> _instances = [];

    private SingleInstance Create()
    {
        var instance = new SingleInstance(_appId);
        _instances.Add(instance);
        return instance;
    }

    public void Dispose() => _instances.ForEach(i => i.Dispose());

    [Fact]
    public void Only_the_first_copy_claims_the_instance()
    {
        Assert.True(Create().TryClaim());
        Assert.False(Create().TryClaim());
    }

    [Fact]
    public async Task A_later_launch_hands_its_arguments_to_the_running_copy()
    {
        var first = Create();
        Assert.True(first.TryClaim());
        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        first.Listen(received.Enqueue);

        var second = Create();
        Assert.False(second.TryClaim());
        Assert.True(second.TryForward(["magnet:?xt=urn:btih:abc&dn=Ubuntu", @"C:\Users\me\Downloads\a b.torrent"], TimeSpan.FromSeconds(5)));

        await TestWait.UntilAsync(() => !received.IsEmpty);
        Assert.Equal(new[] { "magnet:?xt=urn:btih:abc&dn=Ubuntu", @"C:\Users\me\Downloads\a b.torrent" }, received.Single());
    }

    [Fact]
    public async Task The_running_copy_keeps_listening_after_a_handoff()
    {
        var first = Create();
        first.TryClaim();
        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        first.Listen(received.Enqueue);

        Assert.True(Create().TryForward(["one"], TimeSpan.FromSeconds(5)));
        await TestWait.UntilAsync(() => received.Count == 1);
        Assert.True(Create().TryForward([], TimeSpan.FromSeconds(5))); // a plain relaunch: just show the window
        await TestWait.UntilAsync(() => received.Count == 2);

        Assert.Empty(received.Last());
    }

    [Fact]
    public void Forwarding_without_a_running_copy_gives_up_quickly()
    {
        Assert.False(Create().TryForward(["x"], TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void Messages_that_are_not_a_list_of_strings_are_ignored()
    {
        Assert.Equal(new[] { "a", "b" }, SingleInstance.Decode(SingleInstance.Encode(["a", "b"])));
        Assert.Null(SingleInstance.Decode(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(SingleInstance.Decode(Encoding.UTF8.GetBytes("{\"a\":1}")));
    }
}
