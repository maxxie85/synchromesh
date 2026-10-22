using Avalonia.Input.Platform;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

public sealed class ClipboardService : IClipboardService
{
    public async Task<string?> GetTextAsync(CancellationToken ct = default)
    {
        var clipboard = WindowLocator.Active().Clipboard;
        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }
}
