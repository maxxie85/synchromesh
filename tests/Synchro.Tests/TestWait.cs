namespace Synchro.Tests;

internal static class TestWait
{
    /// <summary>Polls a condition so timing-based tests don't depend on fixed sleeps.</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
