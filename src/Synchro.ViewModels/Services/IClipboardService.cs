namespace Synchro.ViewModels.Services;

public interface IClipboardService
{
    Task<string?> GetTextAsync(CancellationToken ct = default);
}
