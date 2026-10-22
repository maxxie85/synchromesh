using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Synchro.ViewModels;

/// <param name="MoveFiles">True moves the data; false only points the torrents at files already in that folder.</param>
public sealed record MoveDataRequest(string Location, bool MoveFiles);

/// <summary>The Move data dialog: a folder on the server, and whether the server should move the files there.</summary>
public sealed partial class MoveDataViewModel : ObservableObject
{
    public MoveDataViewModel(IReadOnlyList<string> torrentNames, string currentLocation)
    {
        Message = torrentNames.Count == 1
            ? $"Move the data of “{torrentNames[0]}” to another folder on the server."
            : $"Move the data of {torrentNames.Count} torrents to another folder on the server.";
        _location = currentLocation;
        CurrentLocation = currentLocation;
    }

    public event EventHandler<MoveDataRequest?>? CloseRequested;

    public string Message { get; }
    public string CurrentLocation { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveCommand))]
    private string _location;

    [ObservableProperty] private bool _moveFiles = true;

    private bool CanMove() => !string.IsNullOrWhiteSpace(Location);

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void Move() => CloseRequested?.Invoke(this, new MoveDataRequest(Location.Trim(), MoveFiles));

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
