using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

/// <summary>The user's answer to the "remove torrents" confirmation.</summary>
public sealed record RemoveRequest(bool DeleteLocalData);

/// <summary>Lets view models open dialogs without knowing anything about the UI framework.</summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the server setup dialog. Pass an existing profile to edit it.
    /// Returns the saved profile, or null if the user cancelled.
    /// </summary>
    Task<ServerProfile?> ShowServerSetupAsync(ServerProfile? existing, CancellationToken ct = default);

    /// <summary>Asks whether to remove the named torrents. Returns null when the user cancels.</summary>
    Task<RemoveRequest?> ConfirmRemoveAsync(IReadOnlyList<string> torrentNames, CancellationToken ct = default);

    /// <summary>
    /// Shows the speed limit dialog at once; it reads the current limits with <paramref name="load"/> while open.
    /// Returns the new limits, or null when the user cancels.
    /// </summary>
    Task<SpeedLimits?> ShowSpeedLimitsAsync(Func<CancellationToken, Task<SpeedLimits>> load, CancellationToken ct = default);

    /// <summary>
    /// Shows the Add torrent dialog. <paramref name="initial"/> pre-fills it (dropped file, pasted link);
    /// null opens it empty. Returns the result, or null when the user cancels.
    /// </summary>
    Task<AddTorrentResult?> ShowAddTorrentAsync(AddTorrentSource? initial, CancellationToken ct = default);

    Task ShowMessageAsync(string title, string message, CancellationToken ct = default);

    /// <summary>Edits the selected torrents' labels. Returns the torrents whose labels changed, or null when cancelled.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> ShowLabelsAsync(
        IReadOnlyList<string> knownLabels, IReadOnlyDictionary<string, IReadOnlyList<string>> labelsByHash, CancellationToken ct = default);

    /// <summary>Asks where to move the torrents' data. Null when the user cancels.</summary>
    Task<MoveDataRequest?> ShowMoveDataAsync(IReadOnlyList<string> torrentNames, string currentLocation, CancellationToken ct = default);

    /// <summary>Asks for a piece of text. Returns it, or null when the user cancels.</summary>
    Task<string?> PromptAsync(TextPrompt prompt, CancellationToken ct = default);

    /// <summary>Shows the app's name and version.</summary>
    Task ShowAboutAsync(CancellationToken ct = default);

    /// <summary>Asks a yes/no question. True when the user chose <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, CancellationToken ct = default);

    /// <summary>Opens the Settings window on <paramref name="section"/>.</summary>
    Task ShowSettingsAsync(Settings.SettingsSection section, CancellationToken ct = default);
}
