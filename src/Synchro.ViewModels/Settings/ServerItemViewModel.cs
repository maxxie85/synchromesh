using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;

namespace Synchro.ViewModels.Settings;

/// <summary>One server in the Settings list and the toolbar's server menu.</summary>
public sealed partial class ServerItemViewModel(ServerProfile profile) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Address))]
    private ServerProfile _profile = profile;

    /// <summary>True for the server the app uses now, whether or not the connection works.</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>The active server's connection state; all false for the others.</summary>
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isFailed;

    /// <summary>"Connected", "Connecting…" or "Can't connect"; empty for servers not in use.</summary>
    [ObservableProperty] private string _stateText = "";

    /// <summary>Why the connection failed, for the marker's tooltip.</summary>
    [ObservableProperty] private string? _stateDetail;

    internal void SetState(bool active, ConnectionState state, string? message)
    {
        IsActive = active;
        IsConnected = active && state == ConnectionState.Connected;
        IsConnecting = active && state == ConnectionState.Connecting;
        IsFailed = active && state == ConnectionState.Failed;
        StateText = !active ? "" : state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting…",
            ConnectionState.Failed => "Can't connect",
            _ => "",
        };
        StateDetail = IsFailed ? message : null;
    }

    public Guid Id => Profile.Id;
    public string Name => Profile.Name;

    /// <summary>"nas.local:9091", with https:// in front when the connection is encrypted.</summary>
    public string Address => $"{(Profile.UseHttps ? "https://" : "")}{Profile.Host}:{Profile.Port}";
}
