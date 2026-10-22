using System.Text.Json;
using Synchro.Core.Servers;

namespace Synchro.AppServices;

/// <summary>Stores server profiles (without passwords) as JSON, written atomically.</summary>
public sealed class JsonServerProfileStore(string filePath) : IServerProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ServerProfile profile, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var profiles = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var index = profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) profiles[index] = profile; else profiles.Add(profile);
            await WriteAsync(profiles, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var profiles = (await ReadAsync(ct).ConfigureAwait(false)).Where(p => p.Id != id).ToList();
            await WriteAsync(profiles, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<ServerProfile>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(filePath)) return [];

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<List<ServerProfile>>(stream, Options, ct).ConfigureAwait(false) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt file shouldn't brick the app: start fresh, the user can re-enter the server.
            return [];
        }
    }

    private async Task WriteAsync(List<ServerProfile> profiles, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var temp = filePath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, profiles, Options, ct).ConfigureAwait(false);

        File.Move(temp, filePath, overwrite: true);
    }
}
