using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Settings;

/// <summary>
/// The Transmission tab in Settings: the connected server's own preferences, a check whether its peer port is
/// reachable, and its blocklist. Edits wait for Apply or Revert.
/// </summary>
public sealed partial class ServerPreferencesViewModel : ObservableValidator
{
    private const string TimePattern = @"^\s*([01]?\d|2[0-3]):[0-5]\d\s*$";
    private const string TimeError = "Enter a time like 07:30.";

    private readonly IServerPreferencesService _preferences;
    private readonly IConnectionService _connection;
    private readonly IDialogService _dialogs;
    private ServerPreferences? _loaded;
    private bool _loading;

    public ServerPreferencesViewModel(IServerPreferencesService preferences, IConnectionService connection, IDialogService dialogs)
    {
        _preferences = preferences;
        _connection = connection;
        _dialogs = dialogs;
        ErrorsChanged += (_, _) => ApplyCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<Choice<EncryptionMode>> EncryptionModes { get; } =
    [
        new(EncryptionMode.Required, "Require encryption"),
        new(EncryptionMode.Preferred, "Prefer encryption"),
        new(EncryptionMode.Allowed, "Allow unencrypted"),
    ];

    /// <summary>Every day, weekdays, weekends, plus the server's own mix when it's something else.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Choice<int>> DayChoices { get; } =
    [
        new(127, "Every day"),
        new(62, "Weekdays"),
        new(65, "Weekends"),
    ];

    [ObservableProperty] private bool _hasPreferences;

    /// <summary>"Preferences of NAS", or why there are none.</summary>
    [ObservableProperty] private string _heading = "";

    [ObservableProperty] private string _downloadDirectory = "";
    [ObservableProperty] private bool _incompleteDirectoryEnabled;
    [ObservableProperty] private string _incompleteDirectory = "";
    [ObservableProperty] private bool _startAddedTorrents;
    [ObservableProperty] private bool _renamePartialFiles;
    [ObservableProperty] private bool _trashOriginalTorrentFiles;

    [ObservableProperty] private bool _downloadQueueEnabled;
    [ObservableProperty, NotifyDataErrorInfo, Range(1, 1000, ErrorMessage = "Enter a number between 1 and 1000.")]
    private int _downloadQueueSize = 5;

    [ObservableProperty] private bool _seedQueueEnabled;
    [ObservableProperty, NotifyDataErrorInfo, Range(1, 1000, ErrorMessage = "Enter a number between 1 and 1000.")]
    private int _seedQueueSize = 10;

    [ObservableProperty] private bool _seedRatioLimited;
    [ObservableProperty, NotifyDataErrorInfo, Range(0.0, 1000.0, ErrorMessage = "Enter a ratio between 0 and 1000.")]
    private double _seedRatioLimit = 2;

    [ObservableProperty] private bool _idleSeedingLimitEnabled;
    [ObservableProperty, NotifyDataErrorInfo, Range(1, 100_000, ErrorMessage = "Enter a number of minutes, at least 1.")]
    private int _idleSeedingLimitMinutes = 30;

    [ObservableProperty, NotifyDataErrorInfo, Range(1, 100_000, ErrorMessage = "Enter a number of peers, at least 1.")]
    private int _peerLimitGlobal = 200;

    [ObservableProperty, NotifyDataErrorInfo, Range(1, 10_000, ErrorMessage = "Enter a number of peers, at least 1.")]
    private int _peerLimitPerTorrent = 50;

    [ObservableProperty] private Choice<EncryptionMode>? _encryption;
    [ObservableProperty] private bool _dhtEnabled;
    [ObservableProperty] private bool _pexEnabled;
    [ObservableProperty] private bool _lpdEnabled;

    [ObservableProperty, NotifyDataErrorInfo, Range(1, 65535, ErrorMessage = "Use a port between 1 and 65535.")]
    private int _peerPort = 51413;

    [ObservableProperty] private bool _portForwardingEnabled;

    [ObservableProperty, NotifyDataErrorInfo, Range(0, 10_000_000, ErrorMessage = "Enter a speed in kB/s.")]
    private int _altSpeedDown = 50;

    [ObservableProperty, NotifyDataErrorInfo, Range(0, 10_000_000, ErrorMessage = "Enter a speed in kB/s.")]
    private int _altSpeedUp = 50;

    [ObservableProperty] private bool _altSpeedTimeEnabled;

    [ObservableProperty, NotifyDataErrorInfo, RegularExpression(TimePattern, ErrorMessage = TimeError)]
    private string _altSpeedFrom = "09:00";

    [ObservableProperty, NotifyDataErrorInfo, RegularExpression(TimePattern, ErrorMessage = TimeError)]
    private string _altSpeedTo = "17:00";

    [ObservableProperty] private Choice<int>? _altSpeedDays;

    [ObservableProperty] private bool _blocklistEnabled;

    [ObservableProperty, NotifyDataErrorInfo]
    [RegularExpression(@"^\s*(https?://\S+)?\s*$", ErrorMessage = "Enter an http:// or https:// address.")]
    private string _blocklistUrl = "";

    /// <summary>"12,345 rules loaded.", or what happened when updating.</summary>
    [ObservableProperty] private string _blocklistText = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RevertCommand), nameof(UpdateBlocklistCommand))]
    private bool _isDirty;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(TestPortCommand), nameof(UpdateBlocklistCommand))]
    private bool _isBusy;

    /// <summary>"Port 51413 is open over IPv4. IPv6: couldn't test."</summary>
    [ObservableProperty] private string _portTestText = "";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        PortTestText = "";
        BlocklistText = "";
        ServerPreferences? loaded;
        try
        {
            loaded = await _preferences.GetAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Show(null, $"Couldn't read the server's preferences: {ex.Message}");
            return;
        }

        Show(loaded, loaded is null ? "Connect to a server to change its preferences." : null);
        if (loaded is null) return;

        try
        {
            BlocklistText = DescribeRules(await _preferences.GetBlocklistSizeAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BlocklistText = ""; // the rest of the tab works; the count is only informative
        }
    }

    private bool CanApply() => IsDirty && !IsBusy && !HasErrors;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        ValidateAllProperties();
        if (HasErrors || Build() is not { } preferences) return;

        IsBusy = true;
        try
        {
            await _preferences.SetAsync(preferences);
            _loaded = preferences;
            IsDirty = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't change the server's preferences", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert()
    {
        if (_loaded is { } loaded) Show(loaded, null);
    }

    private bool CanTestPort() => HasPreferences && !IsBusy;

    /// <summary>Tests the port the server uses now (an unapplied new port isn't in use yet).</summary>
    [RelayCommand(CanExecute = nameof(CanTestPort))]
    private async Task TestPortAsync()
    {
        IsBusy = true;
        PortTestText = "Checking…";
        try
        {
            var results = await _preferences.TestPortAsync();
            var port = (_loaded?.PeerPort ?? PeerPort).ToString(CultureInfo.InvariantCulture);
            PortTestText = string.Join(" ", results.Select(r => r.IsOpen switch
            {
                true => $"Port {port} is open over {Name(r.IpProtocol)}.",
                false => $"Port {port} is closed over {Name(r.IpProtocol)}: peers can't connect to the server.",
                null => $"{Name(r.IpProtocol)}: couldn't test.",
            }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PortTestText = $"Couldn't check the port: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        static string Name(string protocol) => protocol == "ipv6" ? "IPv6" : "IPv4";
    }

    // The server downloads from the address it has saved, so unapplied edits would be ignored: apply first.
    private bool CanUpdateBlocklist() => HasPreferences && !IsBusy && !IsDirty && !string.IsNullOrWhiteSpace(_loaded?.BlocklistUrl);

    [RelayCommand(CanExecute = nameof(CanUpdateBlocklist))]
    private async Task UpdateBlocklistAsync()
    {
        IsBusy = true;
        BlocklistText = "Downloading the blocklist… This can take a minute.";
        try
        {
            BlocklistText = DescribeRules(await _preferences.UpdateBlocklistAsync());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BlocklistText = $"Couldn't update the blocklist: {ex.Message.TrimEnd('.')}. Check the address, and that the server can reach it.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DescribeRules(int count) => count switch
    {
        <= 0 => "No rules loaded.",
        1 => "1 rule loaded.",
        _ => $"{count.ToString("N0", CultureInfo.CurrentCulture)} rules loaded.",
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loading || _loaded is null) return;
        if (e.PropertyName is nameof(IsDirty) or nameof(IsBusy) or nameof(HasPreferences) or nameof(HasErrors)
            or nameof(Heading) or nameof(PortTestText) or nameof(BlocklistText)) return;

        IsDirty = Build() != _loaded;
    }

    private void Show(ServerPreferences? p, string? message)
    {
        _loaded = p;
        HasPreferences = p is not null;
        Heading = message ?? $"Preferences of {_connection.ActiveProfile?.Name ?? "the server"}, for all of its torrents.";
        IsDirty = false;
        TestPortCommand.NotifyCanExecuteChanged();
        UpdateBlocklistCommand.NotifyCanExecuteChanged();
        if (p is null) return;

        _loading = true;
        DownloadDirectory = p.DownloadDirectory;
        IncompleteDirectoryEnabled = p.IncompleteDirectoryEnabled;
        IncompleteDirectory = p.IncompleteDirectory;
        StartAddedTorrents = p.StartAddedTorrents;
        RenamePartialFiles = p.RenamePartialFiles;
        TrashOriginalTorrentFiles = p.TrashOriginalTorrentFiles;
        DownloadQueueEnabled = p.DownloadQueueEnabled;
        DownloadQueueSize = Math.Max(p.DownloadQueueSize, 1);
        SeedQueueEnabled = p.SeedQueueEnabled;
        SeedQueueSize = Math.Max(p.SeedQueueSize, 1);
        SeedRatioLimited = p.SeedRatioLimited;
        SeedRatioLimit = p.SeedRatioLimit;
        IdleSeedingLimitEnabled = p.IdleSeedingLimitEnabled;
        IdleSeedingLimitMinutes = Math.Max(p.IdleSeedingLimitMinutes, 1);
        PeerLimitGlobal = Math.Max(p.PeerLimitGlobal, 1);
        PeerLimitPerTorrent = Math.Max(p.PeerLimitPerTorrent, 1);
        Encryption = EncryptionModes.First(m => m.Value == p.Encryption);
        DhtEnabled = p.DhtEnabled;
        PexEnabled = p.PexEnabled;
        LpdEnabled = p.LpdEnabled;
        PeerPort = p.PeerPort;
        PortForwardingEnabled = p.PortForwardingEnabled;
        AltSpeedDown = p.AltSpeedDownKilobytes;
        AltSpeedUp = p.AltSpeedUpKilobytes;
        AltSpeedTimeEnabled = p.AltSpeedTimeEnabled;
        AltSpeedFrom = FormatTime(p.AltSpeedTimeBegin);
        AltSpeedTo = FormatTime(p.AltSpeedTimeEnd);
        AltSpeedDays = DayChoices.FirstOrDefault(d => d.Value == p.AltSpeedTimeDays) ?? AddCustomDays(p.AltSpeedTimeDays);
        BlocklistEnabled = p.BlocklistEnabled;
        BlocklistUrl = p.BlocklistUrl;
        _loading = false;

        ClearErrors();
        // Values the server sent but a field normalised (e.g. 0 → 1) still count as unchanged: compare to what's shown.
        _loaded = Build() ?? p;
        IsDirty = false;
    }

    private Choice<int> AddCustomDays(int days)
    {
        var custom = new Choice<int>(days, "Custom days");
        DayChoices.Add(custom);
        return custom;
    }

    private ServerPreferences? Build()
    {
        if (!TryParseTime(AltSpeedFrom, out var from) || !TryParseTime(AltSpeedTo, out var to)) return null;

        return new ServerPreferences(
            DownloadDirectory.Trim(), IncompleteDirectoryEnabled, IncompleteDirectory.Trim(), StartAddedTorrents,
            RenamePartialFiles, TrashOriginalTorrentFiles, DownloadQueueEnabled, DownloadQueueSize, SeedQueueEnabled,
            SeedQueueSize, SeedRatioLimited, SeedRatioLimit, IdleSeedingLimitEnabled, IdleSeedingLimitMinutes,
            PeerLimitGlobal, PeerLimitPerTorrent, Encryption?.Value ?? EncryptionMode.Preferred, DhtEnabled, PexEnabled,
            LpdEnabled, PeerPort, PortForwardingEnabled, AltSpeedDown, AltSpeedUp, AltSpeedTimeEnabled, from, to,
            AltSpeedDays?.Value ?? 127, BlocklistEnabled, BlocklistUrl.Trim());
    }

    internal static string FormatTime(int minutes) =>
        $"{Math.Clamp(minutes, 0, 1439) / 60:00}:{Math.Clamp(minutes, 0, 1439) % 60:00}";

    internal static bool TryParseTime(string text, out int minutes)
    {
        minutes = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;

        minutes = h * 60 + m;
        return true;
    }
}
