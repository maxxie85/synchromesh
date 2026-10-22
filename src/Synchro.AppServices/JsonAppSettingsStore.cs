using System.Text.Json;
using System.Text.Json.Serialization;
using Synchro.Core.Settings;

namespace Synchro.AppServices;

/// <summary>Stores <see cref="AppSettings"/> as JSON next to the server profiles, written atomically.</summary>
public sealed class JsonAppSettingsStore(string filePath) : IAppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var updated = change(await ReadAsync(ct).ConfigureAwait(false));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var temp = filePath + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, updated, Options, ct).ConfigureAwait(false);
            File.Move(temp, filePath, overwrite: true);

            return updated;
        }
        finally { _gate.Release(); }
    }

    private async Task<AppSettings> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(filePath)) return new AppSettings();

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, ct).ConfigureAwait(false) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Preferences are easy to set again; a damaged file must not stop the app from starting.
            return new AppSettings();
        }
    }
}
