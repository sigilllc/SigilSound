namespace Sigil.Sound;

/// <summary>
/// Lets a host learn that a script plays a sequence on the host's clock, so it can offer play, pause and stop and a tempo for it.
/// A pattern played without a tempo (<c>pattern.Play(voice)</c>) follows the host; one given a <c>bpm</c> keeps its own time and
/// does not report.
/// </summary>
/// <remarks>
/// Reports happen while the script runs to build its sound, on the thread that runs it, so an observer is per thread and costs
/// nothing when none is set.
/// </remarks>
public static class HostSequence
{
    [ThreadStatic]
    private static Action? _observer;

    /// <summary>Calls <paramref name="onFollowsHost"/> whenever a sequence that follows the host clock is built on this thread, until disposed.</summary>
    public static IDisposable Observe(Action onFollowsHost)
    {
        ArgumentNullException.ThrowIfNull(onFollowsHost);
        var previous = _observer;
        _observer = onFollowsHost;
        return new Scope(previous);
    }

    internal static void ReportFollowsHost() => _observer?.Invoke();

    private sealed class Scope(Action? previous) : IDisposable
    {
        public void Dispose() => _observer = previous;
    }
}
