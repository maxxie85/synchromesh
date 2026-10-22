using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

/// <summary>
/// One row of the torrent list. It is updated in place from each poll, so the UI only
/// re-renders what actually changed instead of rebuilding the whole row.
/// </summary>
public sealed partial class TorrentItemViewModel : ObservableObject
{
    public TorrentItemViewModel(Torrent torrent)
    {
        Hash = torrent.HashString;
        Torrent = torrent;
        Apply(torrent);
    }

    public string Hash { get; }
    public int Id => Torrent.Id;
    public DateTimeOffset AddedDate => Torrent.AddedDate;
    public Torrent Torrent { get; private set; }

    [ObservableProperty] private string _queueText = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private TorrentStatus _status;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progress;          // 0..100, for the progress bar
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _downloadText = "";
    [ObservableProperty] private string _uploadText = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _ratioText = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _errorText;

    /// <summary>"linux, iso" for the row's tooltip; empty without labels.</summary>
    [ObservableProperty] private string _labelsText = "";

    [ObservableProperty] private bool _hasLabels;

    public void Apply(Torrent t)
    {
        Torrent = t;
        var culture = CultureInfo.CurrentCulture;

        QueueText = (t.QueuePosition + 1).ToString(culture);
        Name = t.Name;
        Status = t.Status;
        StatusText = StatusFormatter.Describe(t.Status, t.PercentDone, t.IsStalled);
        Progress = Math.Clamp(t.PercentDone, 0, 1) * 100;
        ProgressText = $"{Progress.ToString("0.#", culture)}%";
        SizeText = ByteFormatter.FormatSize(t.TotalSize);
        DownloadText = t.RateDownload > 0 ? ByteFormatter.FormatRate(t.RateDownload) : "";
        UploadText = t.RateUpload > 0 ? ByteFormatter.FormatRate(t.RateUpload) : "";
        EtaText = t.Status == TorrentStatus.Downloading ? DurationFormatter.FormatEta(t.Eta) : "";
        RatioText = t.UploadRatio < 0 ? "" : t.UploadRatio.ToString("0.00", culture);
        HasError = t.HasError;
        ErrorText = t.HasError ? t.ErrorString : null;
        LabelsText = string.Join(", ", t.LabelList);
        HasLabels = t.LabelList.Count > 0;
    }
}
