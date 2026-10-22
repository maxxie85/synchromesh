using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

public sealed partial class ConfirmRemoveViewModel : ObservableObject
{
    public ConfirmRemoveViewModel(IReadOnlyList<string> names)
    {
        Message = names.Count == 1 ? $"Remove “{names[0]}”?" : $"Remove {names.Count} torrents?";
    }

    public string Message { get; }

    public string Detail =>
        "Removed torrents stop downloading and seeding. The downloaded files stay on the server unless you turn on the switch below.";

    [ObservableProperty] private bool _deleteLocalData;

    /// <summary>The argument is the request, or null when cancelled.</summary>
    public event EventHandler<RemoveRequest?>? CloseRequested;

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(this, new RemoveRequest(DeleteLocalData));

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
