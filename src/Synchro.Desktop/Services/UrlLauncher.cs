using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

public sealed class UrlLauncher : IUrlLauncher
{
    public Task<bool> OpenAsync(Uri url) => WindowLocator.Active().Launcher.LaunchUriAsync(url);
}
