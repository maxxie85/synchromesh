using Synchro.AppServices;
using Synchro.Core.Servers;
using Xunit;

namespace Synchro.Tests;

public sealed class JsonServerProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tm-tests-" + Guid.NewGuid());
    private JsonServerProfileStore CreateStore() => new(Path.Combine(_dir, "profiles.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Returns_nothing_when_the_file_does_not_exist()
        => Assert.Empty(await CreateStore().LoadAsync());

    [Fact]
    public async Task Round_trips_a_profile_and_never_writes_a_password()
    {
        var profile = new ServerProfile(Guid.NewGuid(), "NAS", "nas.local", 9092, true, "/rpc", "admin");

        await CreateStore().SaveAsync(profile);
        var loaded = Assert.Single(await CreateStore().LoadAsync());

        Assert.Equal(profile, loaded);
        Assert.DoesNotContain("password", await File.ReadAllTextAsync(Path.Combine(_dir, "profiles.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_the_same_id_replaces_and_delete_removes()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        await store.SaveAsync(new ServerProfile(id, "Old", "a"));
        await store.SaveAsync(new ServerProfile(id, "New", "b"));

        Assert.Equal("New", Assert.Single(await store.LoadAsync()).Name);

        await store.DeleteAsync(id);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Survives_a_corrupt_file()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "profiles.json"), "{ not json");

        Assert.Empty(await CreateStore().LoadAsync());
    }
}
