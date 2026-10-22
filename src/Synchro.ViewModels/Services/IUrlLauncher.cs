namespace Synchro.ViewModels.Services;

/// <summary>Opens a web page in the user's default browser.</summary>
public interface IUrlLauncher
{
    /// <summary>Returns false when the system could not open the link.</summary>
    Task<bool> OpenAsync(Uri url);
}
