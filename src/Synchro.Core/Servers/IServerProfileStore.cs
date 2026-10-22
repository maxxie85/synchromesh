namespace Synchro.Core.Servers;

public interface IServerProfileStore
{
    Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken ct = default);

    /// <summary>Adds the profile, or replaces the one with the same <see cref="ServerProfile.Id"/>.</summary>
    Task SaveAsync(ServerProfile profile, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
