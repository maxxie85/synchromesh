using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

/// <summary>
/// Receives torrents the OS opens with this app and passes them to <see cref="IExternalAddQueue"/>.
/// On macOS that is Finder's "Open" on a .torrent and clicks on magnet: links. LaunchServices delivers those to the
/// running instance of the .app bundle, which is what keeps the app single-instance there. On Windows a new process
/// starts with the file or link as an argument; <see cref="SingleInstance"/> hands those to <see cref="Forward"/>.
/// </summary>
internal sealed class ActivationListener(IExternalAddQueue queue)
{
    /// <summary>Call before the main loop starts: macOS delivers the launch file or URL as soon as the loop runs.</summary>
    public void Attach(Application app, IReadOnlyList<string> args)
    {
        if (app.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            lifetime.Activated += OnActivated;

        if (args.Count > 0)
            _ = EnqueueAsync(() => ActivationSources.FromArgumentsAsync(args));
    }

    /// <summary>Arguments from a later launch of the app (see <see cref="SingleInstance"/>). Call on the UI thread.</summary>
    public void Forward(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
            _ = EnqueueAsync(() => ActivationSources.FromArgumentsAsync(args));
    }

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        switch (e)
        {
            case FileActivatedEventArgs files:
                var items = files.Files.ToList(); // copy now: the event args may not outlive the handler
                _ = EnqueueAsync(() => ActivationSources.FromStorageItemsAsync(items));
                break;

            case ProtocolActivatedEventArgs protocol:
                _ = EnqueueAsync(() => ActivationSources.FromUriAsync(protocol.Uri));
                break;
        }
    }

    private async Task EnqueueAsync(Func<Task<IReadOnlyList<AddTorrentSource>>> read)
    {
        try
        {
            var sources = await read();
            Dispatcher.UIThread.Post(() => queue.Enqueue(sources));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Reading an opened torrent failed: {ex}");
        }
    }
}
