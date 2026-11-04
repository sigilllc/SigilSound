namespace Sigil.Sound.Operators;

/// <summary>Helpers for sources that follow a <see cref="HostClock"/> instead of counting from their own first frame.</summary>
internal static class HostTime
{
    /// <summary>
    /// The position, in cycles from 0 up to (not including) 1, that an oscillator of constant <paramref name="frequency"/>
    /// has reached at the clock's current frame, had it been running since the clock started. A newly compiled oscillator
    /// starts here, so it continues the phase of the one it replaces.
    /// </summary>
    public static double Cycles(double frequency, HostClock clock)
    {
        double cycles = frequency * clock.SamplePosition / clock.SampleRate;
        return cycles - Math.Floor(cycles);
    }
}
