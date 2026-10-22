using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

/// <summary>The General tab: a progress bar and two columns of facts, Transfer and Torrent.</summary>
public sealed partial class GeneralDetailsViewModel : ObservableObject
{
    private readonly DetailRow _statusRow = new("Status");
    private readonly DetailRow _error = new("Error", wraps: true);
    private readonly DetailRow _have = new("Have");
    private readonly DetailRow _remaining = new("Remaining time");
    private readonly DetailRow _downloaded = new("Downloaded");
    private readonly DetailRow _uploaded = new("Uploaded");
    private readonly DetailRow _ratio = new("Ratio");
    private readonly DetailRow _wasted = new("Wasted");
    private readonly DetailRow _speeds = new("Speed");
    private readonly DetailRow _limits = new("Speed limits");
    private readonly DetailRow _peers = new("Peers");
    private readonly DetailRow _lastActive = new("Last active");

    private readonly DetailRow _location = new("Location", wraps: true);
    private readonly DetailRow _size = new("Size");
    private readonly DetailRow _hash = new("Hash");
    private readonly DetailRow _private = new("Privacy");
    private readonly DetailRow _added = new("Added");
    private readonly DetailRow _completed = new("Completed");
    private readonly DetailRow _created = new("Created");
    private readonly DetailRow _comment = new("Comment", wraps: true);
    private readonly DetailRow _labels = new("Labels", wraps: true);
    private readonly DetailRow _magnet = new("Magnet link");

    public GeneralDetailsViewModel()
    {
        TransferRows = [_statusRow, _error, _have, _remaining, _downloaded, _uploaded, _ratio, _wasted, _speeds, _limits, _peers, _lastActive];
        TorrentRows = [_location, _labels, _size, _hash, _private, _added, _completed, _created, _comment, _magnet];
    }

    public IReadOnlyList<DetailRow> TransferRows { get; }
    public IReadOnlyList<DetailRow> TorrentRows { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _progress; // 0..100
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private TorrentStatus _status;
    [ObservableProperty] private bool _hasError;

    public void Apply(TorrentDetails d)
    {
        var culture = CultureInfo.CurrentCulture;
        var done = Math.Clamp(d.PercentDone, 0, 1);

        Name = d.Name;
        Status = d.Status;
        HasError = d.HasError;
        Progress = done * 100;
        ProgressText = $"{Progress.ToString("0.#", culture)}%";

        _statusRow.Value = StatusFormatter.Describe(d.Status, d.PercentDone, d.IsStalled);
        _error.Value = d.ErrorString ?? "";
        _error.IsVisible = d.HasError;
        _have.Value = d.SizeWhenDone > 0
            ? $"{ByteFormatter.FormatSize(d.SizeWhenDone - d.LeftUntilDone)} of {ByteFormatter.FormatSize(d.SizeWhenDone)} ({ProgressText})"
            : ProgressText;
        _remaining.Value = d.Status == TorrentStatus.Downloading ? DurationFormatter.FormatEta(d.Eta) : DurationFormatter.Unknown;
        _downloaded.Value = ByteFormatter.FormatSize(d.DownloadedEver);
        _uploaded.Value = ByteFormatter.FormatSize(d.UploadedEver);
        _ratio.Value = d.UploadRatio < 0 ? DurationFormatter.Unknown : d.UploadRatio.ToString("0.00", culture);
        _wasted.Value = ByteFormatter.FormatSize(d.CorruptEver);
        _speeds.Value = $"↓ {ByteFormatter.FormatRate(d.RateDownload)}   ↑ {ByteFormatter.FormatRate(d.RateUpload)}";
        _limits.Value = $"↓ {Limit(d.DownloadLimited, d.DownloadLimitKilobytes)}   ↑ {Limit(d.UploadLimited, d.UploadLimitKilobytes)}";
        _peers.Value = d.PeersConnected == 0
            ? "None connected"
            : $"{d.PeersConnected} connected, downloading from {d.PeersSendingToUs}, uploading to {d.PeersGettingFromUs}";
        _lastActive.Value = DateFormatter.Format(d.ActivityDate);

        _location.Value = d.DownloadDirectory;
        _size.Value = d.PieceCount > 0
            ? $"{ByteFormatter.FormatSize(d.TotalSize)} ({d.PieceCount.ToString("N0", culture)} pieces of {ByteFormatter.FormatSize(d.PieceSize)})"
            : ByteFormatter.FormatSize(d.TotalSize);
        _hash.Value = d.HashString;
        _private.Value = d.IsPrivate ? "Private torrent (no DHT or peer exchange)" : "Public torrent";
        _added.Value = DateFormatter.Format(d.AddedDate);
        _completed.Value = DateFormatter.Format(d.DoneDate);
        _created.Value = string.IsNullOrWhiteSpace(d.Creator)
            ? DateFormatter.Format(d.DateCreated)
            : $"{DateFormatter.Format(d.DateCreated)} by {d.Creator}";
        _labels.Value = d.Labels is { Count: > 0 } labels ? string.Join(", ", labels) : "None";
        _comment.Value = d.Comment ?? "";
        _comment.IsVisible = !string.IsNullOrWhiteSpace(d.Comment);
        _magnet.Value = d.MagnetLink ?? "";
    }

    private static string Limit(bool limited, int kilobytesPerSecond)
        => limited ? ByteFormatter.FormatRate(kilobytesPerSecond * 1000L) : "Unlimited";
}
