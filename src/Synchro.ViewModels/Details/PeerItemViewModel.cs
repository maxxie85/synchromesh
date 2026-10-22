using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

public sealed partial class PeerItemViewModel(string key) : ObservableObject
{
    /// <summary>Address and port: what identifies a peer between polls.</summary>
    public string Key { get; } = key;

    public static string KeyOf(PeerInfo p) => $"{p.Address}:{p.Port}";

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
        DownloadText = p.RateToClient > 0 ? ByteFormatter.FormatRate(p.RateToClient) : "";
        UploadText = p.RateToPeer > 0 ? ByteFormatter.FormatRate(p.RateToPeer) : "";
        IsEncrypted = p.IsEncrypted;
    }
}
