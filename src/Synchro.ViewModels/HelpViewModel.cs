using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

/// <summary>About box, Help menu links and the UI stall log.</summary>
public sealed partial class HelpViewModel(
    IDialogService dialogs, IUrlLauncher launcher, IStallLogging stallLog, IAppSettingsStore settings)
    : ObservableObject
{
    /// <summary>Help > Log UI stalls (a checked menu item).</summary>
    public bool LogUiStalls => stallLog.IsEnabled;

    [RelayCommand]
    private async Task ToggleUiStallLogAsync()
    {
        var enabled = !stallLog.IsEnabled;
        stallLog.SetEnabled(enabled);
        OnPropertyChanged(nameof(LogUiStalls));
        try
        {
            await settings.UpdateAsync(s => s with { LogUiStalls = enabled });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logging still runs for this session; it just won't be on after a restart.
            System.Diagnostics.Trace.TraceWarning($"Couldn't save the stall log setting: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task OpenUiStallLogFolderAsync() => stallLog.OpenLogFolderAsync();

    public static readonly Uri TransmissionWebsite = new("https://transmissionbt.com/");
    public static readonly Uri RpcSpecification =
        new("https://github.com/transmission/transmission/blob/main/docs/rpc-spec.md");

    [RelayCommand]
    private Task AboutAsync(CancellationToken ct) => dialogs.ShowAboutAsync(ct);

    [RelayCommand]
    private Task OpenTransmissionWebsiteAsync() => OpenAsync(TransmissionWebsite);

    [RelayCommand]
    private Task OpenRpcSpecificationAsync() => OpenAsync(RpcSpecification);

    private async Task OpenAsync(Uri url)
    {
        if (!await launcher.OpenAsync(url))
            await dialogs.ShowMessageAsync("Couldn't open the link", $"Open {url} in your browser instead.");
    }
}
