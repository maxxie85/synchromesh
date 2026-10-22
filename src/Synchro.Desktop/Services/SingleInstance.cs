using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace Synchro.Desktop.Services;

/// <summary>
/// Keeps one copy of the app per user. A second launch (opening a .torrent or a magnet: link, or starting the app again)
/// hands its arguments to the running copy over a named pipe and exits.
///
/// Windows needs this: it starts a new process for every file or link it opens. macOS doesn't: LaunchServices sends
/// those to the running app itself. Plain .NET (named mutex and pipe), so it also works for a Linux build later.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;

    /// <param name="appId">Unique per app; the user name is added, so each user has their own instance.</param>
    public SingleInstance(string appId) => _name = ShortName(appId, Environment.UserName);

    // Outside Windows a pipe is a socket file in the temp folder, and socket paths are limited to about 100
    // characters; a short hash of app and user keeps the name stable and well within that everywhere.
    private static string ShortName(string appId, string user)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{appId}\n{user}"));
        return "tm-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>True when this is the first copy. It then owns the instance until disposed.</summary>
    public bool TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{_name}", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <summary>Sends the arguments to the running copy. False when it doesn't answer in time.</summary>
    public bool TryForward(IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)timeout.TotalMilliseconds);
            pipe.Write(Encode(args));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"The running copy didn't take the arguments: {ex.Message}");
            return false;
        }
    }

    /// <summary>Accepts arguments from later launches until disposed. <paramref name="received"/> runs on a pool thread.</summary>
    public void Listen(Action<IReadOnlyList<string>> received) => _ = ListenAsync(received, _cts.Token);

    public void Dispose()
    {
        _cts.Cancel();
        _mutex?.Dispose();
        _mutex = null;
    }

    private async Task ListenAsync(Action<IReadOnlyList<string>> received, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);

                using var buffer = new MemoryStream();
                await pipe.CopyToAsync(buffer, ct);
                if (buffer.Length <= MaxMessageBytes && Decode(buffer.ToArray()) is { } args)
                    received(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One broken connection must not stop the listener; wait a moment so a persistent error can't spin.
                Trace.TraceWarning($"Single-instance pipe: {ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            }
        }
    }

    internal static byte[] Encode(IReadOnlyList<string> args) => JsonSerializer.SerializeToUtf8Bytes(args);

    /// <summary>Null for anything that isn't a list of strings: the pipe only accepts this user's processes, but still.</summary>
    internal static IReadOnlyList<string>? Decode(byte[] message)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(message);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
