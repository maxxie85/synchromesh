using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

public sealed partial class PeerItemViewModel(string key) : ObservableObject
{
    /// <summary>Address and port: what identifies a peer between polls.</summary>
    public string Key { get; } = key;

    public static string KeyOf(PeerInfo p) => $"{p.Address}:{p.Port}";

    // Raw speeds for sorting, bytes per second.
    internal long RateDown { get; private set; }
    internal long RateUp { get; private set; }

    /// <summary>Column keys, as the view's columns are tagged.</summary>
    public static class Columns
    {
        public const string Address = "Address", Client = "Client", Flags = "Flags", Progress = "Progress",
            Download = "Download", Upload = "Upload";
    }

    /// <summary>The fastest peers first: the server's own order means nothing.</summary>
    public static TableSort DefaultSort { get; } = new(Columns.Download, true);

    public static bool StartsDescending(string column) => column is Columns.Progress or Columns.Download or Columns.Upload;

    /// <summary>The rows' order for <paramref name="sort"/>; ties by address (and port).</summary>
    public static Comparison<PeerItemViewModel> Order(TableSort sort) => TableSorting.Comparison<PeerItemViewModel>(sort,
        column => column switch
        {
            Columns.Address => (a, b) => NaturalStringComparer.Instance.Compare(a.Address, b.Address),
            Columns.Client => (a, b) => NaturalStringComparer.Instance.Compare(a.Client, b.Client),
            Columns.Flags => (a, b) => string.CompareOrdinal(a.Flags, b.Flags),
            Columns.Progress => (a, b) => a.Progress.CompareTo(b.Progress),
            Columns.Download => (a, b) => a.RateDown.CompareTo(b.RateDown),
            Columns.Upload => (a, b) => a.RateUp.CompareTo(b.RateUp),
            _ => null,
        },
        (a, b) => NaturalStringComparer.Instance.Compare(a.Key, b.Key));

    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _client = "";
    [ObservableProperty] private string _flags = "";
    [ObservableProperty] private double _progress; // 0..100
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _downloadText = "";
    [ObservableProperty] private string _uploadText = "";
    [ObservableProperty] private bool _isEncrypted;

    public void Apply(PeerInfo p)
    {
        Address = p.Address;
        Client = string.IsNullOrWhiteSpace(p.ClientName) ? "Unknown client" : p.ClientName;
        Flags = p.FlagString;
        Progress = Math.Clamp(p.Progress, 0, 1) * 100;
        ProgressText = $"{Progress.ToString("0.#", CultureInfo.CurrentCulture)}%";
        RateDown = p.RateToClient;
        RateUp = p.RateToPeer;
        DownloadText = p.RateToClient > 0 ? ByteFormatter.FormatRate(p.RateToClient) : "";
        UploadText = p.RateToPeer > 0 ? ByteFormatter.FormatRate(p.RateToPeer) : "";
        IsEncrypted = p.IsEncrypted;
    }
}
