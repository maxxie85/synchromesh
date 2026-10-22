namespace Synchro.ViewModels.Services;

/// <summary>
/// The optional log of moments the UI was too busy to respond (Help > Log UI stalls), for tracking down freezes.
/// </summary>
public interface IStallLogging
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);

    /// <summary>Shows the folder with the log in Finder, Explorer or the file manager.</summary>
    Task OpenLogFolderAsync();
}
