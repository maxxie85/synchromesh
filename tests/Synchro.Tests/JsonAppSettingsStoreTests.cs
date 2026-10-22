using Synchro.AppServices;
using Synchro.Core.Settings;
using Xunit;

namespace Synchro.Tests;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tm-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Without_a_file_the_defaults_apply()
    {
        Assert.Equal(new AppSettings(), await new JsonAppSettingsStore(FilePath).LoadAsync());
    }

    [Fact]
    public async Task Updates_are_saved_and_read_back()
    {
        var id = Guid.NewGuid();
        await new JsonAppSettingsStore(FilePath).UpdateAsync(s => s with { Theme = ThemePreference.Dark, LastServerId = id });

        var loaded = await new JsonAppSettingsStore(FilePath).LoadAsync();

        Assert.Equal(new AppSettings(ThemePreference.Dark, id), loaded);
        Assert.Contains("\"dark\"", await File.ReadAllTextAsync(FilePath)); // readable, not a number
    }

    [Fact]
    public async Task Two_updates_keep_each_others_changes()
    {
        var store = new JsonAppSettingsStore(FilePath);
        var id = Guid.NewGuid();

        await Task.WhenAll(
            store.UpdateAsync(s => s with { Theme = ThemePreference.Light }),
            store.UpdateAsync(s => s with { LastServerId = id }));

        Assert.Equal(new AppSettings(ThemePreference.Light, id), await store.LoadAsync());
    }

    [Fact]
    public async Task A_damaged_file_falls_back_to_the_defaults()
    {
        await File.WriteAllTextAsync(FilePath, "{ not json");

        Assert.Equal(new AppSettings(), await new JsonAppSettingsStore(FilePath).LoadAsync());
    }
}
